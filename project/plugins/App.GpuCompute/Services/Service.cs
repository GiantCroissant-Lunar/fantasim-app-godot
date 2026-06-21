using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.GpuCompute.Providers;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.GpuCompute.Services;

/// <summary>
/// Engine-agnostic compute orchestrator. It validates dispatch DTOs and delegates GPU work to the
/// resident backend seam; it never references Godot types.
/// </summary>
public sealed class Service : IService, IDisposable
{
    private readonly IComputeBackend _backend;
    private readonly ILogger _log;
    private bool _disposed;

    public Service(IComputeBackend backend, ILoggerFactory loggerFactory)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _log = loggerFactory.CreateLogger("App.GpuCompute.Service");
    }

    public ComputeCapabilities Capabilities => _backend.Capabilities;

    public event Action<ComputeDispatchResult>? DispatchCompleted;

    public async Task<ComputeDispatchResult> DispatchAsync(
        ComputeDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);

        ComputeDispatchResult result;
        try
        {
            result = await _backend.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Compute dispatch failed for shader {ShaderId}.", request.Shader.ShaderId);
            result = ComputeDispatchResult.Failed(request.Shader.ShaderId, ex.Message);
        }

        DispatchCompleted?.Invoke(result);
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Service));
    }

    private static void Validate(ComputeDispatchRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Shader is null) throw new ArgumentException("Shader is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Shader.ShaderId))
            throw new ArgumentException("ShaderId must not be null or whitespace.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Shader.ShaderPath))
            throw new ArgumentException("ShaderPath must not be null or whitespace.", nameof(request));
        if (request.Groups is null)
            throw new ArgumentException("Groups is required.", nameof(request));
        if (request.Groups.X == 0 || request.Groups.Y == 0 || request.Groups.Z == 0)
            throw new ArgumentException("Dispatch group counts must all be greater than zero.", nameof(request));
        if (request.Buffers is null)
            throw new ArgumentException("Buffers is required.", nameof(request));

        var bindingKeys = new HashSet<(uint Set, uint Binding)>();
        var readbackLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var buffer in request.Buffers)
        {
            if (buffer is null)
                throw new ArgumentException("Buffers must not contain null entries.", nameof(request));
            if (buffer.Data is null)
                throw new ArgumentException("Buffer data must not be null.", nameof(request));
            if (buffer.Data.Length == 0)
                throw new ArgumentException("Buffer data must not be empty.", nameof(request));
            if (buffer.Binding > int.MaxValue)
                throw new ArgumentException("Buffer binding must fit in Godot's signed binding field.", nameof(request));
            if (!bindingKeys.Add((buffer.Set, buffer.Binding)))
                throw new ArgumentException($"Duplicate compute buffer binding set={buffer.Set}, binding={buffer.Binding}.", nameof(request));
            if (buffer.ReadbackBytes is 0)
                throw new ArgumentException("ReadbackBytes must be greater than zero when specified.", nameof(request));
            if (buffer.ReadbackBytes > buffer.Data.Length)
                throw new ArgumentException("ReadbackBytes must not exceed the backing buffer byte length.", nameof(request));
            if (!string.IsNullOrWhiteSpace(buffer.ReadbackLabel) && !readbackLabels.Add(buffer.ReadbackLabel))
                throw new ArgumentException($"Duplicate compute readback label '{buffer.ReadbackLabel}'.", nameof(request));
        }

        var unlabeledReadbacks = request.Buffers.Count(b => b.ReadbackBytes is not null && string.IsNullOrWhiteSpace(b.ReadbackLabel));
        if (unlabeledReadbacks > 0)
            throw new ArgumentException("Every requested readback must have a non-empty ReadbackLabel.", nameof(request));
    }
}
