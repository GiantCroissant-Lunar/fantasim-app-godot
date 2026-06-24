using System;
using System.Linq;
using FantaSim.App.Common;
using FantaSim.App.GpuCompute;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Common.Entry;

public partial class Host : Node
{
    // res:// path of the gpu-demo compute shader. Imported as an RDShaderFile; the resident seam
    // loads it via ResourceLoader.Load<RDShaderFile>() inside the local RenderingDevice.
    private const string GpuSmokeShaderPath = "res://shaders/compute_double.glsl";

    // res:// path of the gpu-demo spatial shader. Loaded directly as a Godot Shader resource; the
    // resident App.GpuShader seam reports its mode (spatial) without compiling it for dispatch.
    private const string GpuShaderSmokeShaderPath = "res://shaders/tint.gdshader";

    private FantaSim.App.GpuCompute.Services.Service? _gpuComputeService;
    private FantaSim.App.GpuShader.Services.Service? _gpuShaderService;

    // GPU smoke (inert unless FANTASIM_GPU_SMOKE=1): dispatch compute_double.glsl over a small uint
    // storage buffer [1,2,3,4] through the composed App.GpuCompute service, read back, and assert each
    // element doubled to [2,4,6,8]. Prints a clear GPU-SMOKE PASS/FAIL line, then quits. Proves the real
    // RenderingDevice path works in the windowed app. Mirrors the FANTASIM_GLOBE_CAPTURE pattern.
    private async void RunGpuSmoke()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_GPU_SMOKE") != "1") return;

        try
        {
            var service = _gpuComputeService
                ?? throw new InvalidOperationException("GPU compute service not composed.");

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

            var request = new ComputeDispatchRequest(
                new ComputeShaderReference("gpu.smoke.double", GpuSmokeShaderPath),
                // compute_double.glsl uses local_size_x = 64; one X group covers up to 64 values.
                new ComputeDispatchSize((uint)((input.Length + 63) / 64), 1, 1),
                new[]
                {
                    new ComputeBufferBinding(0, 0, data, (uint)data.Length, "values"),
                    new ComputeBufferBinding(0, 1, countData),
                });

            var result = await service.DispatchAsync(request);

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

            Callable.From(() =>
            {
                if (verdict.StartsWith("GPU-SMOKE PASS", StringComparison.Ordinal))
                    _log.LogInformation("{Verdict}", verdict);
                else
                    _log.LogError("{Verdict}", verdict);
                _log.LogInformation("{Verdict}", verdict); // ensure the verdict reaches stdout for log scraping even on the error path
                GetTree().Quit();
            }).CallDeferred();
        }
        catch (Exception ex)
        {
            var msg = $"GPU-SMOKE FAIL: {ex.Message}";
            Callable.From(() => { _log.LogError("{Message}", msg); _log.LogInformation("{Message}", msg); GetTree().Quit(); }).CallDeferred();
        }
    }

    // GpuShader smoke (inert unless FANTASIM_GPUSHADER_SMOKE=1): inspect res://shaders/tint.gdshader
    // through the composed App.GpuShader service and assert the resident seam reports its mode as
    // "spatial". Prints a clear GPUSHADER-SMOKE PASS/FAIL line, then quits. Proves the real Godot
    // Shader-resource inspection path works in the windowed app. Mirrors RunGpuSmoke.
    private async void RunGpuShaderSmoke()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_GPUSHADER_SMOKE") != "1") return;

        try
        {
            var service = _gpuShaderService
                ?? throw new InvalidOperationException("GPU shader service not composed.");

            var inspection = await service.InspectShaderAsync(GpuShaderSmokeShaderPath);

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

            Callable.From(() =>
            {
                if (verdict.StartsWith("GPUSHADER-SMOKE PASS", StringComparison.Ordinal))
                    _log.LogInformation("{Verdict}", verdict);
                else
                    _log.LogError("{Verdict}", verdict);
                _log.LogInformation("{Verdict}", verdict); // ensure the verdict reaches stdout for log scraping even on the error path
                GetTree().Quit();
            }).CallDeferred();
        }
        catch (Exception ex)
        {
            var msg = $"GPUSHADER-SMOKE FAIL: {ex.Message}";
            Callable.From(() => { _log.LogError("{Message}", msg); _log.LogInformation("{Message}", msg); GetTree().Quit(); }).CallDeferred();
        }
    }
}
