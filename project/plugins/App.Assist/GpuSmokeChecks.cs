using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Assist;

/// <summary>
/// Dev-only GPU smoke checks. Mirrors the former host-side <c>Host.Gpu.cs</c> logic but lives in
/// the App.Assist bundle, where it can resolve the GPU services from the shared kernel registry.
/// Each check is inert unless its corresponding environment variable is set to <c>1</c>.
/// </summary>
internal sealed class GpuSmokeChecks
{
    // res:// path of the gpu-demo compute shader. Imported as an RDShaderFile; the resident seam
    // loads it via ResourceLoader.Load<RDShaderFile>() inside the local RenderingDevice.
    private const string GpuSmokeShaderPath = "res://shaders/compute_double.glsl";

    // res:// path of the gpu-demo spatial shader. Loaded directly as a Godot Shader resource; the
    // resident App.GpuShader seam reports its mode (spatial) without compiling it for dispatch.
    private const string GpuShaderSmokeShaderPath = "res://shaders/tint.gdshader";

    private readonly IRegistry _kernel;
    private readonly ILogger _log;
    private readonly CrosscutFoundation.Config.IService? _config;

    public GpuSmokeChecks(IRegistry kernel, ILogger log)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        // Smoke flags come from the layered crosscut config (gpu:smoke / gpu:shaderSmoke in app.json,
        // Env-overridable). Resolved from the shared kernel; null -> checks stay inert.
        _config = kernel.TryGet<CrosscutFoundation.Config.IService>();
    }

    // GPU smoke (inert unless FANTASIM_GPU_SMOKE=1): dispatch compute_double.glsl over a small uint
    // storage buffer [1,2,3,4] through the composed App.GpuCompute service, read back, and assert each
    // element doubled to [2,4,6,8]. Prints a clear GPU-SMOKE PASS/FAIL line, then exits. Proves the real
    // RenderingDevice path works in the windowed app. Mirrors the former FANTASIM_GLOBE_CAPTURE pattern.
    public async Task RunComputeSmokeAsync(CancellationToken cancellationToken = default)
    {
        if (_config?.GetValue("gpu:smoke", false) != true) return;

        try
        {
            var service = _kernel.TryGet<FantaSim.App.GpuCompute.IService>();
            if (service is null)
            {
                Exit("GPU-SMOKE FAIL: GPU compute service not registered.");
                return;
            }

            var caps = service.Capabilities;
            _log.LogInformation(
                "gpu-smoke backend={BackendName} available={IsAvailable} reason={UnavailableReason}",
                caps.BackendName,
                caps.IsAvailable,
                caps.UnavailableReason);

            uint[] input = { 1, 2, 3, 4 };
            uint[] expected = input.Select(v => v * 2u).ToArray();

            var data = new byte[input.Length * sizeof(uint)];
            Buffer.BlockCopy(input, 0, data, 0, data.Length);

            // Element count for the shader's bounds guard (set 0, binding 1).
            var countData = new byte[sizeof(uint)];
            Buffer.BlockCopy(new[] { (uint)input.Length }, 0, countData, 0, countData.Length);

            var request = new FantaSim.App.GpuCompute.ComputeDispatchRequest(
                new FantaSim.App.GpuCompute.ComputeShaderReference("gpu.smoke.double", GpuSmokeShaderPath),
                // compute_double.glsl uses local_size_x = 64; one X group covers up to 64 values.
                new FantaSim.App.GpuCompute.ComputeDispatchSize((uint)((input.Length + 63) / 64), 1, 1),
                new[]
                {
                    new FantaSim.App.GpuCompute.ComputeBufferBinding(0, 0, data, (uint)data.Length, "values"),
                    new FantaSim.App.GpuCompute.ComputeBufferBinding(0, 1, countData),
                });

            var result = await service.DispatchAsync(request, cancellationToken).ConfigureAwait(false);

            string verdict;
            if (!result.Succeeded)
            {
                verdict = $"GPU-SMOKE FAIL: dispatch failed: {result.ErrorMessage}";
            }
            else if (!result.ReadbackBuffers.TryGetValue("values", out var bytes))
            {
                verdict = "GPU-SMOKE FAIL: no 'values' readback returned.";
            }
            else
            {
                var readback = new uint[bytes.Length / sizeof(uint)];
                Buffer.BlockCopy(bytes, 0, readback, 0, readback.Length * sizeof(uint));
                verdict = readback.SequenceEqual(expected)
                    ? $"GPU-SMOKE PASS: input=[{string.Join(",", input)}] readback=[{string.Join(",", readback)}]"
                    : $"GPU-SMOKE FAIL: input=[{string.Join(",", input)}] expected=[{string.Join(",", expected)}] readback=[{string.Join(",", readback)}]";
            }

            Exit(verdict);
        }
        catch (Exception ex)
        {
            Exit($"GPU-SMOKE FAIL: {ex.Message}");
        }
    }

    // GpuShader smoke (inert unless FANTASIM_GPUSHADER_SMOKE=1): inspect res://shaders/tint.gdshader
    // through the composed App.GpuShader service and assert the resident seam reports its mode as
    // "spatial". Prints a clear GPUSHADER-SMOKE PASS/FAIL line, then exits. Mirrors RunComputeSmokeAsync.
    public async Task RunShaderSmokeAsync(CancellationToken cancellationToken = default)
    {
        if (_config?.GetValue("gpu:shaderSmoke", false) != true) return;

        try
        {
            var service = _kernel.TryGet<FantaSim.App.GpuShader.IService>();
            if (service is null)
            {
                Exit("GPUSHADER-SMOKE FAIL: GPU shader service not registered.");
                return;
            }

            var inspection = await service.InspectShaderAsync(GpuShaderSmokeShaderPath, cancellationToken).ConfigureAwait(false);

            string verdict;
            if (!inspection.Found)
            {
                verdict = $"GPUSHADER-SMOKE FAIL: shader not found at {GpuShaderSmokeShaderPath}: {inspection.Error}";
            }
            else if (!string.Equals(inspection.ShaderKind, "spatial", StringComparison.Ordinal))
            {
                verdict = $"GPUSHADER-SMOKE FAIL: expected mode=spatial, got mode={inspection.ShaderKind} (len={inspection.SourceLength})";
            }
            else
            {
                verdict = $"GPUSHADER-SMOKE PASS: mode=spatial (len={inspection.SourceLength})";
            }

            Exit(verdict);
        }
        catch (Exception ex)
        {
            Exit($"GPUSHADER-SMOKE FAIL: {ex.Message}");
        }
    }

    private void Exit(string verdict)
    {
        var passed = verdict.StartsWith("GPU-SMOKE PASS", StringComparison.Ordinal)
                  || verdict.StartsWith("GPUSHADER-SMOKE PASS", StringComparison.Ordinal);

        if (passed)
            _log.LogInformation("{Verdict}", verdict);
        else
            _log.LogError("{Verdict}", verdict);

        // Ensure the verdict reaches stdout for log scraping even on the error path.
        _log.LogInformation("{Verdict}", verdict);

        System.Environment.Exit(passed ? 0 : 1);
    }
}
