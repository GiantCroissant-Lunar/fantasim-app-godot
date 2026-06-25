using System.Collections.Generic;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>Pinned VPLanet first-slice external-tool manifest.</summary>
public static class VplanetExternalToolManifest
{
    public static ExternalToolManifest Build()
        => new(
            ToolId: "vplanet",
            ToolVersion: "2.5.36",
            Provider: "iii",
            License: "MIT",
            SourceUrl: "https://github.com/VirtualPlanetaryLaboratory/vplanet",
            Functions: new[]
            {
                new ExternalToolFunctionManifest(
                    FunctionId: "vplanet.status",
                    Label: "VPLanet Status",
                    Category: "external/science",
                    Summary: "Check installed VPLanet executable version and modules.",
                    IsSideEffect: false,
                    IsExpensive: false,
                    Inputs: System.Array.Empty<ExternalToolPortManifest>(),
                    Outputs: new[] { new ExternalToolPortManifest("status", "Status", "vplanet/status", Required: false) },
                    State: new ExternalToolStateManifest(Progress: false, Logs: true, Artifacts: false, Warnings: false),
                    ExecutionTraits: new FunctionExecutionTraits(
                        RequiresExternalProcess: true)),

                new ExternalToolFunctionManifest(
                    FunctionId: "vplanet.input.build",
                    Label: "Build VPLanet Input",
                    Category: "external/science",
                    Summary: "Build primary and body input files from structured parameters.",
                    IsSideEffect: false,
                    IsExpensive: false,
                    Inputs: System.Array.Empty<ExternalToolPortManifest>(),
                    Outputs: new[] { new ExternalToolPortManifest("inputBundle", "Input Bundle", "vplanet/input-bundle", Required: false) },
                    Parameters: new[]
                    {
                        new ExternalToolParameterManifest("systemName", "System Name", "string", "solarsystem"),
                        new ExternalToolParameterManifest("starBodyName", "Star Body Name", "string", "sun"),
                        new ExternalToolParameterManifest("planetBodyName", "Planet Body Name", "string", "earth"),
                        new ExternalToolParameterManifest("stopTimeYears", "Stop Time Years", "double", "4.6e9"),
                        new ExternalToolParameterManifest("outputTimeYears", "Output Time Years", "double", "1.0e6"),
                    },
                    State: new ExternalToolStateManifest(Progress: false, Logs: false, Artifacts: true, Warnings: false),
                    ExecutionTraits: new FunctionExecutionTraits(
                        RequiresExternalProcess: false,
                        IsDeterministic: true,
                        ArtifactShape: "vplanet/input-bundle")),

                new ExternalToolFunctionManifest(
                    FunctionId: "vplanet.run",
                    Label: "Run VPLanet",
                    Category: "external/science",
                    Summary: "Run a VPLanet scenario and return output artifact references.",
                    IsSideEffect: true,
                    IsExpensive: true,
                    Inputs: new[] { new ExternalToolPortManifest("inputBundle", "Input Bundle", "vplanet/input-bundle", Required: true) },
                    Outputs: new[] { new ExternalToolPortManifest("runResult", "Run Result", "vplanet/run-result", Required: false) },
                    Parameters: new[]
                    {
                        new ExternalToolParameterManifest("timeoutSeconds", "Timeout", "int", "300"),
                    },
                    State: new ExternalToolStateManifest(Progress: true, Logs: true, Artifacts: true, Warnings: true),
                    ExecutionTraits: new FunctionExecutionTraits(
                        RequiresExternalProcess: true,
                        SupportsCancellation: true,
                        DefaultTimeoutSeconds: 300,
                        ArtifactShape: "vplanet/run-result",
                        CommitEligibility: "adapter-gated")),

                new ExternalToolFunctionManifest(
                    FunctionId: "vplanet.output.parse",
                    Label: "Parse VPLanet Output",
                    Category: "external/science",
                    Summary: "Parse VPLanet logs and output files into a normalized table.",
                    IsSideEffect: false,
                    IsExpensive: false,
                    Inputs: new[] { new ExternalToolPortManifest("runResult", "Run Result", "vplanet/run-result", Required: true) },
                    Outputs: new[] { new ExternalToolPortManifest("outputTable", "Output Table", "vplanet/output-table", Required: false) },
                    Parameters: new[]
                    {
                        new ExternalToolParameterManifest("bodyName", "Body Name", "string", "sun"),
                    },
                    State: new ExternalToolStateManifest(Progress: false, Logs: false, Artifacts: false, Warnings: false),
                    ExecutionTraits: new FunctionExecutionTraits(
                        RequiresExternalProcess: false,
                        IsDeterministic: true,
                        ArtifactShape: "vplanet/output-table")),
            },
            ProviderMetadata: new FunctionProviderMetadata(
                ProviderKind: FunctionProviderKinds.Iii,
                ProviderId: "vplanet-worker",
                RuntimeRequirement: "python3:vplanet>=2.5",
                Determinism: "versioned",
                TrustLevel: "external-service"));
}
