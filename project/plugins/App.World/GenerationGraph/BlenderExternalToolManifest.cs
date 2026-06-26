using System.Collections.Generic;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>Pinned Blender first-slice external-tool manifest.
/// Function ids match the real <c>blender.refine</c> and <c>asset.to_gltf</c>
/// functions registered by <c>project/workers/blender-worker/worker.py</c>;
/// the iii axis claims the <c>blender.</c> and <c>asset.</c> families via
/// <see cref="IiiFunctionProvider"/> and <c>workers.json</c>.</summary>
public static class BlenderExternalToolManifest
{
    public static ExternalToolManifest Build()
        => new(
            ToolId: "blender",
            ToolVersion: "headless",
            Provider: "iii",
            License: "GPL-3.0",
            SourceUrl: "https://www.blender.org",
            Functions: new[]
            {
                new ExternalToolFunctionManifest(
                    FunctionId: "blender.refine",
                    Label: "Blender Refine Mesh",
                    Category: "external/geometry",
                    Summary: "Refine a TripoSR .obj into a game-ready mesh and export USD (.usdc).",
                    IsSideEffect: true,
                    IsExpensive: true,
                    Inputs: new[]
                    {
                        new ExternalToolPortManifest("source", "Source Mesh", "comfy/mesh", Required: true),
                    },
                    Outputs: new[]
                    {
                        new ExternalToolPortManifest("usdPath", "USD Path", "blender/usd", Required: false),
                    },
                    Parameters: new[]
                    {
                        new ExternalToolParameterManifest("jobId", "Job Id", "string", "job"),
                    },
                    State: new ExternalToolStateManifest(Progress: false, Logs: true, Artifacts: true, Warnings: false),
                    ExecutionTraits: new FunctionExecutionTraits(
                        RequiresExternalProcess: true,
                        IsDeterministic: true,
                        DefaultTimeoutSeconds: 180,
                        ArtifactShape: "blender/usd",
                        CommitEligibility: "adapter-gated")),

                new ExternalToolFunctionManifest(
                    FunctionId: "asset.to_gltf",
                    Label: "Asset To glTF",
                    Category: "external/geometry",
                    Summary: "Convert any supported source (USD/FBX/OBJ/glTF) into glTF for Godot import.",
                    IsSideEffect: true,
                    IsExpensive: true,
                    Inputs: new[]
                    {
                        new ExternalToolPortManifest("source", "Source Asset", "blender/usd", Required: true),
                    },
                    Outputs: new[]
                    {
                        new ExternalToolPortManifest("glbPath", "glTF Path", "asset/gltf", Required: false),
                    },
                    Parameters: new[]
                    {
                        new ExternalToolParameterManifest("jobId", "Job Id", "string", "job"),
                    },
                    State: new ExternalToolStateManifest(Progress: false, Logs: true, Artifacts: true, Warnings: false),
                    ExecutionTraits: new FunctionExecutionTraits(
                        RequiresExternalProcess: true,
                        IsDeterministic: true,
                        DefaultTimeoutSeconds: 180,
                        ArtifactShape: "asset/gltf",
                        CommitEligibility: "adapter-gated")),
            },
            ProviderMetadata: new FunctionProviderMetadata(
                ProviderKind: FunctionProviderKinds.Iii,
                ProviderId: "blender-worker",
                RuntimeRequirement: "python3:blender-headless",
                Determinism: "versioned",
                TrustLevel: "external-service"));
}