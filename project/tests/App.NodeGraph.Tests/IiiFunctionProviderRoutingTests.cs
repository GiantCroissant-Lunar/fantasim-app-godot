using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Iii;
using FantaSim.App.World;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.NodeGraph.Tests;

public sealed class IiiFunctionProviderRoutingTests
{
    private sealed class FakeInvoker : IIiiInvoker
    {
        public string? FunctionId { get; private set; }
        public JsonObject? Payload { get; private set; }

        public Task<JsonObject> RequestAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)
        {
            FunctionId = functionId;
            Payload = payload;
            return Task.FromResult(new JsonObject
            {
                ["ok"] = true,
                ["functionId"] = functionId,
            });
        }
    }

    [Fact]
    public void Supports_VplanetFunctions_AndRejectsWorldFunctions()
    {
        var provider = new IiiFunctionProvider(new FakeInvoker());

        Assert.True(provider.Supports("vplanet.status"));
        Assert.True(provider.Supports("vplanet.run"));
        Assert.False(provider.Supports("world.options"));
    }

    [Fact]
    public async Task InvokeAsync_ForwardsVplanetRequestToInvoker()
    {
        var fake = new FakeInvoker();
        var provider = new IiiFunctionProvider(fake);

        var result = await provider.InvokeAsync("vplanet.status", new JsonObject { ["probe"] = "1" });

        Assert.Equal("vplanet.status", fake.FunctionId);
        Assert.Equal("1", fake.Payload!["probe"]!.GetValue<string>());
        Assert.True(result["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void VplanetManifest_EveryFunction_IsSupportedByIiiFunctionProvider()
    {
        var manifest = VplanetExternalToolManifest.Build();
        var provider = new IiiFunctionProvider(new FakeInvoker());

        Assert.All(manifest.Functions, function =>
            Assert.True(provider.Supports(function.FunctionId)));
    }

    [Fact]
    public void VplanetManifest_ProjectedSchemas_PreserveExpectedFunctionIds()
    {
        var manifest = VplanetExternalToolManifest.Build();
        var schemas = ExternalToolNodeSchemaProjector.Project(manifest);

        Assert.Equal(
            new[] { "vplanet.status", "vplanet.input.build", "vplanet.run", "vplanet.output.parse" },
            schemas.Select(schema => schema.TypeId).ToArray());
    }

    [Fact]
    public void VplanetInputBuild_HasExpectedParametersAndOutput()
    {
        var function = VplanetExternalToolManifest.Build().Functions
            .Single(f => f.FunctionId == "vplanet.input.build");

        Assert.Empty(function.Inputs);
        Assert.Single(function.Outputs, port => port.PortId == "inputBundle");
        Assert.Equal(
            new[] { "systemName", "starBodyName", "planetBodyName", "stopTimeYears", "outputTimeYears" },
            function.Parameters!.Select(p => p.Key).ToArray());
    }

    [Fact]
    public void VplanetRun_HasExpectedInputParameterAndOutput()
    {
        var function = VplanetExternalToolManifest.Build().Functions
            .Single(f => f.FunctionId == "vplanet.run");

        Assert.Single(function.Inputs, port => port.PortId == "inputBundle");
        Assert.Single(function.Outputs, port => port.PortId == "runResult");
        Assert.Single(function.Parameters!, parameter => parameter.Key == "timeoutSeconds");
    }

    [Fact]
    public void VplanetOutputParse_HasExpectedInputParameterAndOutput()
    {
        var function = VplanetExternalToolManifest.Build().Functions
            .Single(f => f.FunctionId == "vplanet.output.parse");

        Assert.Single(function.Inputs, port => port.PortId == "runResult");
        Assert.Single(function.Outputs, port => port.PortId == "outputTable");
        Assert.Single(function.Parameters!, parameter => parameter.Key == "bodyName");
    }

    [Fact]
    public void VplanetManifest_HasNoDuplicateFunctionIds()
    {
        var manifest = VplanetExternalToolManifest.Build();
        var functionIds = manifest.Functions.Select(f => f.FunctionId).ToList();

        Assert.Equal(functionIds.Count, new HashSet<string>(functionIds).Count);
    }

    [Fact]
    public void VplanetFunctions_HaveNoDuplicatePortsOrParameters()
    {
        var manifest = VplanetExternalToolManifest.Build();

        Assert.All(manifest.Functions, function =>
        {
            Assert.Equal(function.Inputs.Count, new HashSet<string>(function.Inputs.Select(p => p.PortId)).Count);
            Assert.Equal(function.Outputs.Count, new HashSet<string>(function.Outputs.Select(p => p.PortId)).Count);

            if (function.Parameters != null)
            {
                Assert.Equal(function.Parameters.Count, new HashSet<string>(function.Parameters.Select(p => p.Key)).Count);
            }
        });
    }
}
