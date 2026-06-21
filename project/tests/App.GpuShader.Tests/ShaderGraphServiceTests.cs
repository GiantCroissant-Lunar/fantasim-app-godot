using FantaSim.App.GpuShader;
using FantaSim.App.GpuShader.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.GpuShader.Tests;

public sealed class ShaderGraphServiceTests
{
    [Fact]
    public async Task GetShaderGraphAsync_ReturnsDefaultMaterialGraph()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);

        var graph = await service.GetShaderGraphAsync();
        var validation = await service.ValidateShaderGraphAsync(graph);

        Assert.Equal("gpu-shader.default", graph.GraphId);
        Assert.Contains(graph.Nodes, node => node.TypeId == "shader.canvas-item");
        Assert.Contains(graph.Nodes, node => node.TypeId == "input.vertex-color");
        Assert.Contains(graph.Nodes, node => node.TypeId == "output.fragment-color");
        Assert.True(validation.Succeeded, string.Join(Environment.NewLine, validation.Issues.Select(i => $"{i.Code}: {i.Message}")));
    }

    [Fact]
    public async Task ApplyShaderGraphEditAsync_AddNodeRaisesShaderGraphChanged()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);
        var calls = 0;
        service.ShaderGraphChanged += () => calls++;

        var graph = await service.ApplyShaderGraphEditAsync(new GpuShaderGraphEdit("add-node", TypeId: "buffer.storage"));

        Assert.Equal(1, calls);
        Assert.Contains(graph.Nodes, node => node.NodeId == "buffer_storage" && node.TypeId == "buffer.storage");
    }

    [Fact]
    public async Task ApplyShaderGraphEditAsync_InvalidEditDoesNotRaiseShaderGraphChanged()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);
        var calls = 0;
        service.ShaderGraphChanged += () => calls++;

        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyShaderGraphEditAsync(
            new GpuShaderGraphEdit("remove-node", NodeId: "missing")));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task GetShaderNodeCatalogAsync_ContainsMaterialAndDispatchNodes()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);

        var catalog = await service.GetShaderNodeCatalogAsync();

        var fragment = Assert.Single(catalog, node => node.TypeId == "output.fragment-color");
        Assert.Contains(fragment.Inputs, port => port.PortId == "color" && port.KindHint == "gpu/vec4" && port.Required);

        var dispatch = Assert.Single(catalog, node => node.TypeId == "gpu.dispatch");
        Assert.Contains(dispatch.Inputs, port => port.PortId == "shader" && port.Required);
        Assert.Contains(dispatch.Inputs, port => port.PortId == "groups" && port.Required);
        Assert.Contains(dispatch.Inputs, port => port.PortId == "buffers" && port.KindHint == "gpu/storage-buffer[]");
    }

    [Fact]
    public async Task ValidateShaderGraphAsync_AcceptsMinimalDispatchGraph()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);
        var catalog = await service.GetShaderNodeCatalogAsync();
        var graph = new GpuShaderGraphView(
            "graph.test",
            "Test Graph",
            "Minimal compute dispatch graph.",
            new[]
            {
                NodeFromSchema(catalog, "shader", "shader.compute"),
                NodeFromSchema(catalog, "groups", "dispatch.groups"),
                NodeFromSchema(catalog, "dispatch", "gpu.dispatch"),
            },
            new[]
            {
                new GpuShaderGraphWire("shader", "shader", "dispatch", "shader", "gpu/compute-shader"),
                new GpuShaderGraphWire("groups", "groups", "dispatch", "groups", "gpu/dispatch-groups"),
            });

        var result = await service.ValidateShaderGraphAsync(graph);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(i => $"{i.Code}: {i.Message}")));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ValidateShaderGraphAsync_RejectsMissingRequiredDispatchInput()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);
        var catalog = await service.GetShaderNodeCatalogAsync();
        var graph = new GpuShaderGraphView(
            "graph.test",
            "Test Graph",
            "Missing dispatch groups.",
            new[]
            {
                NodeFromSchema(catalog, "shader", "shader.compute"),
                NodeFromSchema(catalog, "dispatch", "gpu.dispatch"),
            },
            new[]
            {
                new GpuShaderGraphWire("shader", "shader", "dispatch", "shader", "gpu/compute-shader"),
            });

        var result = await service.ValidateShaderGraphAsync(graph);

        Assert.False(result.Succeeded);
        var issue = Assert.Single(result.Issues, i => i.Code == "MissingRequiredInput");
        Assert.Equal("dispatch", issue.NodeId);
        Assert.Equal("groups", issue.PortId);
    }

    [Fact]
    public async Task ValidateShaderGraphAsync_RejectsCycles()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);
        var catalog = await service.GetShaderNodeCatalogAsync();
        var shader = NodeFromSchema(catalog, "shader", "shader.compute") with
        {
            Inputs = new[] { new GpuShaderGraphPort("feedback", "Feedback", "gpu/dispatch-result", Required: false) },
        };
        var graph = new GpuShaderGraphView(
            "graph.test",
            "Test Graph",
            "Feedback is invalid for a static dispatch graph.",
            new[]
            {
                shader,
                NodeFromSchema(catalog, "groups", "dispatch.groups"),
                NodeFromSchema(catalog, "dispatch", "gpu.dispatch"),
            },
            new[]
            {
                new GpuShaderGraphWire("shader", "shader", "dispatch", "shader", "gpu/compute-shader"),
                new GpuShaderGraphWire("groups", "groups", "dispatch", "groups", "gpu/dispatch-groups"),
                new GpuShaderGraphWire("dispatch", "result", "shader", "feedback", "gpu/dispatch-result"),
            });

        var result = await service.ValidateShaderGraphAsync(graph);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "CycleDetected");
    }

    [Fact]
    public async Task ValidateShaderGraphAsync_ReportsNullTypeIdAsIssueInsteadOfThrowing()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);
        var graph = new GpuShaderGraphView(
            "graph.test",
            "Test Graph",
            "Node with a null TypeId (e.g. from deserialized remote input).",
            new[]
            {
                new GpuShaderGraphNode(
                    "broken",
                    TypeId: null!,
                    "Broken",
                    "Test",
                    IsSideEffect: false,
                    IsExpensive: false,
                    Inputs: Array.Empty<GpuShaderGraphPort>(),
                    Outputs: Array.Empty<GpuShaderGraphPort>()),
            },
            Array.Empty<GpuShaderGraphWire>());

        var result = await service.ValidateShaderGraphAsync(graph);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "NodeTypeRequired" && issue.NodeId == "broken");
    }

    private static GpuShaderGraphNode NodeFromSchema(
        IReadOnlyList<GpuShaderNodeSchema> catalog,
        string nodeId,
        string typeId)
    {
        var schema = Assert.Single(catalog, node => node.TypeId == typeId);
        return new GpuShaderGraphNode(
            nodeId,
            schema.TypeId,
            schema.Label,
            schema.Category,
            schema.IsSideEffect,
            schema.IsExpensive,
            schema.Inputs,
            schema.Outputs,
            schema.Parameters);
    }

    [Fact]
    public async Task InspectShaderAsync_ForwardsToBackendAndRejectsEmptyPath()
    {
        using var backend = new FakeBackend();
        using var service = new FantaSim.App.GpuShader.Services.Service(backend, NullLoggerFactory.Instance);

        var inspection = await service.InspectShaderAsync("res://bundles/gpu-demo/shaders/tint.gdshader");

        Assert.True(inspection.Found);
        Assert.Equal("canvas_item", inspection.ShaderKind);
        Assert.Equal("res://bundles/gpu-demo/shaders/tint.gdshader", backend.LastInspectedPath);

        await Assert.ThrowsAsync<ArgumentException>(() => service.InspectShaderAsync("  "));
    }

    private sealed class FakeBackend : IShaderGraphBackend
    {
        public string? LastInspectedPath { get; private set; }

        public Task<GpuShaderInspection> InspectShaderAsync(
            string resourcePath,
            CancellationToken cancellationToken = default)
        {
            LastInspectedPath = resourcePath;
            return Task.FromResult(new GpuShaderInspection(resourcePath, Found: true, "canvas_item", 100));
        }

        public void Dispose()
        {
        }
    }
}
