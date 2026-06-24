using System.Collections.Generic;
using System.Linq;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>Projects a generic external-tool manifest onto world-generation node schemas.</summary>
public static class ExternalToolNodeSchemaProjector
{
    public static IReadOnlyList<WorldGenerationNodeSchema> Project(ExternalToolManifest manifest)
        => manifest.Functions.Select(ProjectFunction).ToList();

    private static WorldGenerationNodeSchema ProjectFunction(ExternalToolFunctionManifest function)
        => new(
            TypeId: function.FunctionId,
            Label: function.Label,
            Category: function.Category,
            IsSideEffect: function.IsSideEffect,
            IsExpensive: function.IsExpensive,
            Inputs: function.Inputs.Select(p => new WorldGenerationGraphPort(p.PortId, p.Label, p.Kind, p.Required)).ToList(),
            Outputs: function.Outputs.Select(p => new WorldGenerationGraphPort(p.PortId, p.Label, p.Kind, p.Required)).ToList(),
            Summary: function.Summary,
            Parameters: function.Parameters?.Select(p => new WorldGenerationGraphParameter(p.Key, p.Label, p.DefaultValue, p.Kind)).ToList());
}
