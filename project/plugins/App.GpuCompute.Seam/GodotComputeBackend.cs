using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.GpuCompute.Providers;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.GpuCompute.Seam;

/// <summary>
/// Resident Godot RenderingDevice backend for App.GpuCompute. All Godot types and native RIDs stay here.
/// </summary>
public sealed class GodotComputeBackend : IComputeBackend
{
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _deviceLock = new();
    private RenderingDevice? _device;
    private bool _disposed;

    // Cache keyed by $"{shaderId}|{shaderPath}|{version}".
    // All access happens while holding _gate (DispatchCore already runs under it), so no extra lock is needed.
    private readonly Dictionary<string, (Rid Shader, Rid Pipeline)> _pipelineCache = new();
    private readonly List<string> _lruOrder = new(); // front = oldest (LRU), back = newest (MRU)
    private const int MaxCacheEntries = 8;

    public GodotComputeBackend(ILoggerFactory loggerFactory)
    {
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _log = loggerFactory.CreateLogger("App.GpuCompute.GodotComputeBackend");
    }

    public ComputeCapabilities Capabilities
    {
        get
        {
            var device = EnsureDevice();
            return device is null
                ? new ComputeCapabilities(false, "Godot RenderingDevice", "RenderingDevice is unavailable; headless and Compatibility renderer modes do not provide it.")
                : new ComputeCapabilities(true, "Godot RenderingDevice");
        }
    }

    /// <summary>
    /// Frees and clears all cached shader+pipeline RIDs synchronously under the gate.
    /// Exists for future hot-reload invalidation; not wired to anything today.
    /// </summary>
    internal void ClearPipelineCache()
    {
        _gate.Wait();
        try
        {
            if (_device is null)
            {
                _pipelineCache.Clear();
                _lruOrder.Clear();
                return;
            }
            foreach (var key in _lruOrder)
            {
                if (_pipelineCache.TryGetValue(key, out var entry))
                {
                    if (entry.Pipeline.IsValid)
                        _device.FreeRid(entry.Pipeline);
                    if (entry.Shader.IsValid)
                        _device.FreeRid(entry.Shader);
                }
            }
            _pipelineCache.Clear();
            _lruOrder.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ComputeDispatchResult> DispatchAsync(
        ComputeDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GodotComputeBackend));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rd = EnsureDevice();
            if (rd is null)
                return ComputeDispatchResult.Failed(request.Shader.ShaderId, "RenderingDevice is unavailable in this runtime.");

            return DispatchCore(rd, request);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        lock (_deviceLock)
        {
            // Free cached pipeline+shader RIDs before freeing the device.
            foreach (var key in _lruOrder)
            {
                if (_pipelineCache.TryGetValue(key, out var entry))
                {
                    if (entry.Pipeline.IsValid)
                        _device?.FreeRid(entry.Pipeline);
                    if (entry.Shader.IsValid)
                        _device?.FreeRid(entry.Shader);
                }
            }
            _pipelineCache.Clear();
            _lruOrder.Clear();

            // A local RenderingDevice is a plain GodotObject (not RefCounted): Dispose() would only
            // release the C# wrapper and leak the native device, so Free() it.
            _device?.Free();
            _device = null;
        }
    }

    private RenderingDevice? EnsureDevice()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GodotComputeBackend));
        // Capabilities reads are not serialized by the dispatch gate, so creation needs its own lock.
        lock (_deviceLock)
        {
            if (_device is not null)
                return _device;

            _device = RenderingServer.CreateLocalRenderingDevice();
            if (_device is null)
                _log.LogWarning("Godot local RenderingDevice is unavailable.");
            return _device;
        }
    }

    private ComputeDispatchResult DispatchCore(RenderingDevice rd, ComputeDispatchRequest request)
    {
        var shaderId = request.Shader.ShaderId;
        Rid shaderRid = default;
        Rid pipelineRid = default;
        var bufferRids = new List<(ComputeBufferBinding Binding, Rid Rid)>();
        var uniformSetRids = new List<(uint Set, Rid Rid)>();
        long computeList = -1;
        var computeListOpen = false;
        // True whenever the CACHE owns the shader+pipeline RIDs (cache hit OR successful
        // insert) — the finally block must never free cache-owned RIDs, only the partial
        // leftovers of a failed creation.
        var ownedByCache = false;
        var cacheKey = $"{request.Shader.ShaderId}|{request.Shader.ShaderPath}|{request.Shader.Version}";

        try
        {
            if (_pipelineCache.TryGetValue(cacheKey, out var cached))
            {
                shaderRid = cached.Shader;
                pipelineRid = cached.Pipeline;
                ownedByCache = true;
                _lruOrder.Remove(cacheKey);
                _lruOrder.Add(cacheKey);
            }
            else
            {
                var shaderFile = ResourceLoader.Load<RDShaderFile>(
                    request.Shader.ShaderPath,
                    "",
                    ResourceLoader.CacheMode.Replace);
                if (shaderFile is null)
                    return ComputeDispatchResult.Failed(shaderId, $"RDShaderFile not found: {request.Shader.ShaderPath}");
                if (!string.IsNullOrWhiteSpace(shaderFile.BaseError))
                    return ComputeDispatchResult.Failed(shaderId, shaderFile.BaseError);

                var version = string.IsNullOrWhiteSpace(request.Shader.Version)
                    ? new StringName("")
                    : new StringName(request.Shader.Version);
                var spirv = shaderFile.GetSpirV(version);
                if (spirv is null)
                    return ComputeDispatchResult.Failed(shaderId, $"Shader SPIR-V not available: {request.Shader.ShaderPath}");
                if (!string.IsNullOrWhiteSpace(spirv.CompileErrorCompute))
                    return ComputeDispatchResult.Failed(shaderId, spirv.CompileErrorCompute);

                shaderRid = rd.ShaderCreateFromSpirV(spirv, shaderId);
                if (!shaderRid.IsValid)
                    return ComputeDispatchResult.Failed(shaderId, $"Shader RID creation failed: {request.Shader.ShaderPath}");

                pipelineRid = rd.ComputePipelineCreate(shaderRid, new Godot.Collections.Array<RDPipelineSpecializationConstant>());
                if (!pipelineRid.IsValid)
                    return ComputeDispatchResult.Failed(shaderId, "Compute pipeline RID creation failed.");

                if (_lruOrder.Count >= MaxCacheEntries)
                {
                    var evictKey = _lruOrder[0];
                    _lruOrder.RemoveAt(0);
                    if (_pipelineCache.Remove(evictKey, out var evicted))
                    {
                        if (evicted.Pipeline.IsValid)
                            rd.FreeRid(evicted.Pipeline);
                        if (evicted.Shader.IsValid)
                            rd.FreeRid(evicted.Shader);
                    }
                }
                _pipelineCache[cacheKey] = (shaderRid, pipelineRid);
                _lruOrder.Add(cacheKey);
                ownedByCache = true;
            }

            foreach (var buffer in request.Buffers)
            {
                var rid = rd.StorageBufferCreate(
                    (uint)buffer.Data.Length,
                    buffer.Data,
                    (RenderingDevice.StorageBufferUsage)0);
                if (!rid.IsValid)
                    return ComputeDispatchResult.Failed(shaderId, $"Storage buffer RID creation failed for binding {buffer.Set}:{buffer.Binding}.");
                bufferRids.Add((buffer, rid));
            }

            foreach (var setGroup in bufferRids.GroupBy(item => item.Binding.Set).OrderBy(group => group.Key))
            {
                var uniforms = new Godot.Collections.Array<RDUniform>();
                foreach (var item in setGroup.OrderBy(item => item.Binding.Binding))
                {
                    var uniform = new RDUniform
                    {
                        UniformType = RenderingDevice.UniformType.StorageBuffer,
                        Binding = checked((int)item.Binding.Binding),
                    };
                    uniform.AddId(item.Rid);
                    uniforms.Add(uniform);
                }

                var uniformSetRid = rd.UniformSetCreate(uniforms, shaderRid, setGroup.Key);
                if (!uniformSetRid.IsValid)
                    return ComputeDispatchResult.Failed(shaderId, $"Uniform set RID creation failed for set {setGroup.Key}.");
                uniformSetRids.Add((setGroup.Key, uniformSetRid));
            }

            computeList = rd.ComputeListBegin();
            computeListOpen = true;
            rd.ComputeListBindComputePipeline(computeList, pipelineRid);
            foreach (var uniformSet in uniformSetRids.OrderBy(item => item.Set))
                rd.ComputeListBindUniformSet(computeList, uniformSet.Rid, uniformSet.Set);
            rd.ComputeListDispatch(computeList, request.Groups.X, request.Groups.Y, request.Groups.Z);
            rd.ComputeListEnd();
            computeListOpen = false;

            rd.Submit();
            rd.Sync();

            var readbacks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var item in bufferRids)
            {
                if (item.Binding.ReadbackBytes is not { } readbackBytes ||
                    string.IsNullOrWhiteSpace(item.Binding.ReadbackLabel))
                    continue;
                readbacks[item.Binding.ReadbackLabel] = rd.BufferGetData(item.Rid, 0, readbackBytes);
            }

            return new ComputeDispatchResult(shaderId, true, readbacks);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Godot compute dispatch failed for shader {ShaderId}.", shaderId);
            if (computeListOpen)
            {
                try { rd.ComputeListEnd(); }
                catch { }
            }
            return ComputeDispatchResult.Failed(shaderId, ex.Message);
        }
        finally
        {
            foreach (var uniformSet in uniformSetRids)
                FreeRidIfValid(rd, uniformSet.Rid);
            if (!ownedByCache)
            {
                // Creation failed partway (never cached): free the partial RIDs.
                FreeRidIfValid(rd, pipelineRid);
                FreeRidIfValid(rd, shaderRid);
            }
            foreach (var buffer in bufferRids)
                FreeRidIfValid(rd, buffer.Rid);
        }
    }

    private static void FreeRidIfValid(RenderingDevice rd, Rid rid)
    {
        if (rid.IsValid)
            rd.FreeRid(rid);
    }
}
