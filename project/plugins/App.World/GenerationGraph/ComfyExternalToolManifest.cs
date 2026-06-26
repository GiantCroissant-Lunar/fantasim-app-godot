using System.Collections.Generic;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>Pinned ComfyUI first-slice external-tool manifest.
/// The function id matches the real <c>comfy.generate</c> function registered by
/// <c>project/workers/comfy-worker/worker.py</c>; the iii axis claims the
/// <c>comfy.</c> family via <see cref="IiiFunctionProvider"/> and <c>workers.json</c>.</summary>
public static class ComfyExternalToolManifest
{
    public static ExternalToolManifest Build()
        => new(
            ToolId: "comfy",
            ToolVersion: "sdxl+tripoSR",
            Provider: "iii",
            License: null,
            SourceUrl: "https://github.com/comfyanonymous/ComfyUI",
            Functions: new[]
            {
                new ExternalToolFunctionManifest(
                    FunctionId: "comfy.generate",
                    Label: "ComfyUI Generate Mesh",
                    Category: "external/imagine",
                    Summary: "Run SDXL txt2img -> TripoSR in ComfyUI and return a generated mesh (.obj) plus preview image.",
                    IsSideEffect: true,
                    IsExpensive: true,
                    Inputs: System.Array.Empty<ExternalToolPortManifest>(),
                    Outputs: new[]
                    {
                        new ExternalToolPortManifest("mesh", "Mesh (.obj)", "comfy/mesh", Required: false),
                        new ExternalToolPortManifest("image", "Preview Image", "comfy/image", Required: false),
                    },
                    Parameters: new[]
                    {
                        new ExternalToolParameterManifest("prompt", "Prompt", "string", ""),
                        new ExternalToolParameterManifest("jobId", "Job Id", "string", "job"),
                    },
                    State: new ExternalToolStateManifest(Progress: true, Logs: true, Artifacts: true, Warnings: true),
                    ExecutionTraits: new FunctionExecutionTraits(
                        RequiresExternalProcess: true,
                        RequiresNetwork: true,
                        SupportsCancellation: true,
                        DefaultTimeoutSeconds: 600,
                        ArtifactShape: "comfy/mesh",
                        CommitEligibility: "adapter-gated")),
            },
            ProviderMetadata: new FunctionProviderMetadata(
                ProviderKind: FunctionProviderKinds.Iii,
                ProviderId: "comfy-worker",
                RuntimeRequirement: "python3:comfyui+tripoSR",
                Determinism: "stochastic",
                TrustLevel: "external-service"));
}