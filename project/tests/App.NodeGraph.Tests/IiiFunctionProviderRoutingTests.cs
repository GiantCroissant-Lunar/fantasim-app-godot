using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Iii;
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
}
