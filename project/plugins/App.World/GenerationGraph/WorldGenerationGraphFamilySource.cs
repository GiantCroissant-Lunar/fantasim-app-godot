using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Editable graph source for a selected graph inside a world-generation family document.
/// The generic UI sees the effective graph for the active tick; edits are applied to the
/// authored graph, then the effective graph is recomposed.
/// </summary>
public sealed class WorldGenerationGraphFamilySource : IGraphSource, IGraphAnnotationSource, IGraphSubgraphSource
{
    private readonly object _gate = new();
    private WorldGenerationGraphSource _activeSource = null!;

    public WorldGenerationGraphFamilySource(
        string sourceId,
        WorldGenerationGraphFamilyDocument family,
        string activeGraphId,
        long activeTick)
    {
        SourceId = string.IsNullOrWhiteSpace(sourceId)
            ? throw new ArgumentException("Source id must be non-empty.", nameof(sourceId))
            : sourceId;
        Family = family ?? throw new ArgumentNullException(nameof(family));
        ActiveGraphId = RequireNonEmpty(activeGraphId, nameof(activeGraphId));
        ActiveTick = activeTick;
        RecomposeLocked();
    }

    public string SourceId { get; }

    public WorldGenerationGraphFamilyDocument Family { get; private set; }

    public string ActiveGraphId { get; private set; }

    public string ActiveGraphLabel => Graph.Label;

    public long ActiveTick { get; private set; }

    public IReadOnlyList<string> CompositionWarnings { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<WorldGenerationSubgraphBinding> ActiveSubgraphs { get; private set; } =
        Array.Empty<WorldGenerationSubgraphBinding>();

    public IReadOnlyList<GraphSubgraph> Subgraphs { get; private set; } = Array.Empty<GraphSubgraph>();

    public WorldGenerationGraphView Graph => _activeSource.Graph;

    public GraphDocument Document => _activeSource.Document;

    public IReadOnlyList<GraphAnnotation> Annotations => _activeSource.Annotations;

    public event Action? Changed;

    public static WorldGenerationGraphFamilySource ForRegime(
        string sourceId,
        WorldGenerationGraphFamilyDocument family,
        string scheduleKind,
        string regimeId,
        long tick,
        string? sphereId = null)
    {
        var graph = WorldGenerationGraphFamilyComposer.ResolveRegimeGraph(
            family,
            scheduleKind,
            regimeId,
            sphereId);

        return new WorldGenerationGraphFamilySource(sourceId, family, graph.GraphId, tick);
    }

    public void SelectGraph(string graphId, long? tick = null)
    {
        lock (_gate)
        {
            ActiveGraphId = RequireNonEmpty(graphId, nameof(graphId));
            if (tick.HasValue)
                ActiveTick = tick.Value;
            RecomposeLocked();
        }

        Changed?.Invoke();
    }

    void IGraphSubgraphSource.SelectGraph(string graphId) => SelectGraph(graphId);

    public void SelectRegime(string scheduleKind, string regimeId, long tick, string? sphereId = null)
    {
        var graph = WorldGenerationGraphFamilyComposer.ResolveRegimeGraph(
            Family,
            scheduleKind,
            regimeId,
            sphereId);

        SelectGraph(graph.GraphId, tick);
    }

    public void SetTick(long tick)
    {
        lock (_gate)
        {
            if (ActiveTick == tick)
                return;

            ActiveTick = tick;
            RecomposeLocked();
        }

        Changed?.Invoke();
    }

    public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(edit);

        lock (_gate)
        {
            var authoredGraph = ResolveAuthoredGraph(Family, ActiveGraphId);
            var editedGraph = WorldGenerationGraphSource.ApplyEdit(authoredGraph, edit);
            Family = ReplaceAuthoredGraph(Family, ActiveGraphId, editedGraph);
            RecomposeLocked();
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public CompiledWorldGenerationGraph CompileForExecution(string? sinkNodeId = null)
        => WorldGenerationGraphCompiler.Compile(Graph, sinkNodeId);

    public WorldGenerationGraphExecutionScopeKey? TryBuildExecutionScopeKey(
        string variant = "base",
        string branch = "main",
        int scheduleRevision = 1)
    {
        lock (_gate)
        {
            var binding = ResolveRegimeBindingForGraph(Family, ActiveGraphId);
            if (binding is null)
                return null;

            return new WorldGenerationGraphExecutionScopeKey(
                binding.ScheduleKind,
                binding.RegimeId,
                ActiveGraphId,
                Family.Revision,
                scheduleRevision,
                variant,
                branch);
        }
    }

    private void RecomposeLocked()
    {
        var composition = WorldGenerationGraphFamilyComposer.ComposeEffectiveGraph(
            Family,
            ActiveGraphId,
            ActiveTick);

        _activeSource = new WorldGenerationGraphSource(SourceId, composition.Graph);
        CompositionWarnings = composition.Warnings;
        ActiveSubgraphs = WorldGenerationGraphFamilyComposer.ListSubgraphs(Family, ActiveGraphId);
        Subgraphs = MapSubgraphs(Family, ActiveSubgraphs);
    }

    private static IReadOnlyList<GraphSubgraph> MapSubgraphs(
        WorldGenerationGraphFamilyDocument family,
        IReadOnlyList<WorldGenerationSubgraphBinding> bindings)
        => bindings
            .Select(binding =>
            {
                var graph = TryResolveFamilyGraph(family, binding.SubgraphId);
                return new GraphSubgraph(
                    binding.ParentGraphId,
                    binding.NodeId,
                    binding.SubgraphId,
                    graph?.Label ?? binding.SubgraphId,
                    graph?.Description,
                    binding.InputPortMap,
                    binding.OutputPortMap);
            })
            .ToList();

    private static WorldGenerationGraphView? TryResolveFamilyGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId)
    {
        if (string.Equals(family.BaseGraph.GraphId, graphId, StringComparison.Ordinal))
            return family.BaseGraph;

        return family.Graphs.FirstOrDefault(candidate =>
            string.Equals(candidate.GraphId, graphId, StringComparison.Ordinal));
    }

    private static WorldRegimeGraphBinding? ResolveRegimeBindingForGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId)
        => ResolveRegimeBindingForGraph(family, graphId, new HashSet<string>(StringComparer.Ordinal));

    private static WorldRegimeGraphBinding? ResolveRegimeBindingForGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId,
        ISet<string> visited)
    {
        if (!visited.Add(graphId))
            return null;

        var direct = family.RegimeGraphBindings.FirstOrDefault(binding =>
            string.Equals(binding.GraphId, graphId, StringComparison.Ordinal));
        if (direct is not null)
            return direct;

        foreach (var subgraph in family.SubgraphBindings ?? Array.Empty<WorldGenerationSubgraphBinding>())
        {
            if (!string.Equals(subgraph.SubgraphId, graphId, StringComparison.Ordinal))
                continue;

            var parentBinding = ResolveRegimeBindingForGraph(family, subgraph.ParentGraphId, visited);
            if (parentBinding is not null)
                return parentBinding;
        }

        return null;
    }

    private static WorldGenerationGraphView ResolveAuthoredGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId)
    {
        if (string.Equals(family.BaseGraph.GraphId, graphId, StringComparison.Ordinal))
            return family.BaseGraph;

        var graph = family.Graphs.FirstOrDefault(candidate =>
            string.Equals(candidate.GraphId, graphId, StringComparison.Ordinal));

        return graph ?? throw new ArgumentException(
            $"Graph '{graphId}' does not exist in family '{family.DocumentId}'.");
    }

    private static WorldGenerationGraphFamilyDocument ReplaceAuthoredGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId,
        WorldGenerationGraphView graph,
        DateTimeOffset? updatedUtc = null)
    {
        var stamp = updatedUtc ?? DateTimeOffset.UnixEpoch;
        var revision = family.Revision + 1;
        if (string.Equals(family.BaseGraph.GraphId, graphId, StringComparison.Ordinal))
        {
            return family with
            {
                Revision = revision,
                BaseGraph = graph,
                UpdatedUtc = stamp,
            };
        }

        var graphs = family.Graphs.ToList();
        var index = graphs.FindIndex(candidate => string.Equals(candidate.GraphId, graphId, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"Graph '{graphId}' does not exist in family '{family.DocumentId}'.");

        graphs[index] = graph;
        return family with
        {
            Revision = revision,
            Graphs = graphs,
            UpdatedUtc = stamp,
        };
    }

    private static string RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be non-empty.", parameterName);

        return value;
    }
}
