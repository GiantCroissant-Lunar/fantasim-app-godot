#if USE_PROJECT_REFERENCES
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Arch.Core;
using FantaSim.App.Ecs.Fields;
using FantaSim.App.Ecs.Systems;
using FantaSim.World.Fields;
using FantaSim.World.Fields.Core;
using FantaSim.World.TruthStream;
using FantaSim.World.TruthStream.Core;
using TimeDete.Time.Primitives;
using Xunit;
using UnifyECS;

namespace FantaSim.App.Ecs.Tests;

/// <summary>
/// Determinism + cross-path proof for <see cref="ReduceFieldsSystem"/>.
/// The system groups contributions by (Field, Subject, Tick), sorts each group canonically by
/// (Tick, ProducerId), reduces via the pure <see cref="IFieldReducer"/>, writes
/// <see cref="ResolvedFields"/>, and clears the contribution buffer. These tests pin:
/// <list type="bullet">
/// <item>Shuffled contributions reduce to the same value (canonical sort makes order not matter).</item>
/// <item>Canonical sort is load-bearing: a non-canonical order yields a different result for a
///       last-wins reducer (this is the failing-first proof — without the sort, determinism breaks).</item>
/// <item>The buffer is cleared after reduction (per-tick lifecycle).</item>
/// <item>Direct reduce equals the truth-stream-backed materialize-then-reduce path (cross-path).</item>
/// </list>
/// All paths use the real fantasim-world <see cref="CompositeFieldCatalog"/>,
/// <see cref="FieldReducerRegistry"/>, and <see cref="DefaultReducers"/>.
/// </summary>
public class ReduceFieldsSystemTests
{
    private static readonly FieldId Elevation = new("app.elevation-m");
    private static readonly SubjectRef Cell = new("cell", "c1");

    private static (IFieldCatalog catalog, IFieldReducerRegistry reducers) Compose()
    {
        var reducers = new FieldReducerRegistry();
        DefaultReducers.RegisterAll(reducers);
        var descriptor = new FieldDescriptor(
            Elevation, "m", WellKnownReducers.WeightedAverage, ValueKind.Continuous);
        var catalog = new CompositeFieldCatalog(new[] { descriptor });
        CatalogValidator.Validate(catalog, reducers);
        return (catalog, reducers);
    }

    private static FieldContribution C(double value, string producer, double weight = 1.0,
        OriginTag origin = OriginTag.Derived, long tick = 7) =>
        new(Elevation, Cell, new CanonicalTick(tick), value, origin, weight, producer);

    private static (Arch.Core.World world, Arch.Core.Entity cell, FieldContributionBuffer buffer, ResolvedFields resolved)
        NewCellWith(List<FieldContribution> seed)
    {
        var world = Arch.Core.World.Create();
        var resolved = new ResolvedFields();
        var buffer = new FieldContributionBuffer(seed);
        var cell = world.Create(new FieldSubject(Cell), buffer, resolved);
        return (world, cell, buffer, resolved);
    }

    // ---------------------------------------------------------------------
    // Behavior 1: Shuffled contributions reduce to the same value because the
    // system canonical-sorts each group by (Tick, ProducerId) before reduce.
    // WeightedAverage is commutative, so this also pins grouping correctness.
    // ---------------------------------------------------------------------
    [Fact]
    public void Shuffled_contributions_reduce_to_identical_value_under_canonical_sort()
    {
        var (catalog, reducers) = Compose();
        var system = new ReduceFieldsSystem(catalog, reducers);

        var ordered = new List<FieldContribution>
        {
            C(1.0, "p-a"), C(2.0, "p-b"), C(3.0, "p-c"),
        };
        var shuffled = new List<FieldContribution> { ordered[2], ordered[0], ordered[1] };

        var (worldOrdered, _, _, resolvedOrdered) = NewCellWith(new List<FieldContribution>(ordered));
        var (worldShuffled, _, _, resolvedShuffled) = NewCellWith(shuffled);

        system.Execute(worldOrdered, 0f);
        system.Execute(worldShuffled, 0f);

        Assert.True(resolvedShuffled.ByField.TryGetValue(Elevation, out var shuffledVal));
        Assert.True(resolvedOrdered.ByField.TryGetValue(Elevation, out var orderedVal));
        Assert.Equal(orderedVal.Value, shuffledVal.Value);
        Assert.Equal(3, resolvedShuffled.ByField[Elevation].ContributionCount);
    }

    // ---------------------------------------------------------------------
    // Behavior 2 (failing-first proof): the system's canonical sort is what
    // makes an order-sensitive reducer deterministic. The built-in reducers are
    // either commutative or canonicalize internally, so they cannot show the
    // sort is load-bearing. A test-only FirstValue reducer is non-deterministic
    // over a raw (shuffled) list but deterministic over the canonically-sorted
    // list the system feeds it. This proves: without the sort, the result would
    // depend on input order (the failing-first claim); with it, it does not.
    // ---------------------------------------------------------------------
    [Fact]
    public void Canonical_sort_makes_order_sensitive_reducer_deterministic()
    {
        var reducers = new FieldReducerRegistry();
        // Register only the test-only reducer; the catalog references it.
        var firstValueReducer = new FirstValueReducer();
        reducers.Register(firstValueReducer);
        var descriptor = new FieldDescriptor(
            Elevation, "m", firstValueReducer.Id, ValueKind.Continuous);
        var catalog = new CompositeFieldCatalog(new[] { descriptor });
        CatalogValidator.Validate(catalog, reducers);

        var aThenB = new List<FieldContribution>
        {
            C(1.0, "p-a"), C(2.0, "p-b"),
        };
        var bThenA = new List<FieldContribution> { aThenB[1], aThenB[0] };

        // Without canonical sort, FirstValue returns the input's first element:
        // aThenB -> 1.0, bThenA -> 2.0. Order-dependent (non-deterministic).
        Assert.Equal(1.0, firstValueReducer.Reduce(descriptor, aThenB).Value);
        Assert.Equal(2.0, firstValueReducer.Reduce(descriptor, bThenA).Value);

        // The system canonical-sorts before feeding the reducer, so both shuffled
        // inputs resolve to the canonical-first producer's value ("p-a" -> 1.0).
        var system = new ReduceFieldsSystem(catalog, reducers);
        var (worldA, _, _, resolvedA) = NewCellWith(new List<FieldContribution>(aThenB));
        var (worldB, _, _, resolvedB) = NewCellWith(new List<FieldContribution>(bThenA));
        system.Execute(worldA, 0f);
        system.Execute(worldB, 0f);

        Assert.Equal(resolvedA.ByField[Elevation].Value, resolvedB.ByField[Elevation].Value);
        Assert.Equal(1.0, resolvedA.ByField[Elevation].Value);
        Assert.Equal(OriginTag.Derived, resolvedA.ByField[Elevation].ResolvedOrigin);
    }

    // ---------------------------------------------------------------------
    // Behavior 3: After reduction, the contribution buffer is cleared in place
    // (per-tick buffer-and-clear lifecycle).
    // ---------------------------------------------------------------------
    [Fact]
    public void Reduce_clears_contribution_buffer_in_place()
    {
        var (catalog, reducers) = Compose();
        var system = new ReduceFieldsSystem(catalog, reducers);
        var contributions = new List<FieldContribution> { C(1.0, "p-a"), C(2.0, "p-b") };
        var (world, _, buffer, _) = NewCellWith(contributions);

        Assert.Equal(2, buffer.Contributions.Count);
        system.Execute(world, 0f);
        Assert.Empty(buffer.Contributions);
    }

    // ---------------------------------------------------------------------
    // Behavior 4 (cross-path determinism): direct reduce equals the
    // truth-stream-backed materialize-then-reduce path for identical contributions.
    // The same pure reducer serves the live ECS path and the headless event-sourced path.
    // ---------------------------------------------------------------------
    [Fact]
    public void Direct_reduce_equals_truth_stream_materialized_reduce()
    {
        var (catalog, reducers) = Compose();
        if (!reducers.TryGet(WellKnownReducers.WeightedAverage, out var reducer))
            throw new Xunit.Sdk.XunitException("weighted-average reducer not registered");
        if (!catalog.TryGet(Elevation, out var descriptor))
            throw new Xunit.Sdk.XunitException("elevation descriptor missing");

        var contributions = new List<FieldContribution>
        {
            C(1.0, "p-a", weight: 2.0),
            C(4.0, "p-b", weight: 1.0),
            C(3.0, "p-c", weight: 3.0),
        };

        // Path A — direct reduce via the live ECS system.
        var system = new ReduceFieldsSystem(catalog, reducers);
        var (world, _, _, resolved) = NewCellWith(new List<FieldContribution>(contributions));
        system.Execute(world, 0f);
        var direct = resolved.ByField[Elevation];

        // Path B — truth-stream-backed: encode contributions as events, append, materialize,
        // decode back to contributions, canonical-sort, reduce. Same reducer, same ordered input.
        var streamId = new TruthStreamIdentity("v", "main", 0, "app-ecs", "reduce-test");
        var store = new InMemoryTruthEventStore();
        var drafts = contributions.Select(c => new TruthEventDraft(
            streamId, "field.contribution", Encode(c), c.Tick)).ToArray();
        store.AppendAsync(streamId, drafts).GetAwaiter().GetResult();

        var materialized = new List<FieldContribution>();
        var materializer = new DelegateMaterializer<List<FieldContribution>>((acc, evt) =>
        {
            if (evt.EventType == "field.contribution")
                acc.Add(Decode(evt.Payload));
        });
        var replayed = materializer.MaterializeAsync(store.ReadAsync(streamId))
            .GetAwaiter().GetResult();
        replayed.Sort(CompareCanonically);
        var truthStream = reducer.Reduce(descriptor, replayed);

        Assert.Equal(direct.Value, truthStream.Value);
        Assert.Equal(direct.ContributionCount, truthStream.ContributionCount);
        Assert.Equal(direct.Subject, truthStream.Subject);
        Assert.Equal(direct.Tick, truthStream.Tick);
    }

    private static int CompareCanonically(FieldContribution a, FieldContribution b)
    {
        var tickCmp = a.Tick.CompareTo(b.Tick);
        if (tickCmp != 0) return tickCmp;
        return string.Compare(a.ProducerId, b.ProducerId, StringComparison.Ordinal);
    }

    private static byte[] Encode(FieldContribution c)
    {
        var s = string.Join('|',
            c.Field.Value, c.Subject.Kind, c.Subject.Id, c.Tick.Value, c.Value,
            (int)c.Origin, c.Weight, c.ProducerId);
        return Encoding.UTF8.GetBytes(s);
    }

    private static FieldContribution Decode(ReadOnlyMemory<byte> payload)
    {
        var parts = Encoding.UTF8.GetString(payload.Span).Split('|');
        return new FieldContribution(
            Field: new FieldId(parts[0]),
            Subject: new SubjectRef(parts[1], parts[2]),
            Tick: new CanonicalTick(long.Parse(parts[3])),
            Value: double.Parse(parts[4]),
            Origin: (OriginTag)int.Parse(parts[5]),
            Weight: double.Parse(parts[6]),
            ProducerId: parts[7]);
    }

    /// <summary>
    /// Test-only reducer that returns the first contribution's value. Order-sensitive:
    /// non-deterministic over a raw list, deterministic over the canonically-sorted list the
    /// system feeds it. Exists only to prove the system's canonical sort is load-bearing
    /// (the built-in reducers are all order-independent, so they cannot show this).
    /// </summary>
    private sealed class FirstValueReducer : IFieldReducer
    {
        public ReducerId Id { get; } = new("test.first-value");
        public IReadOnlyList<ValueKind> AcceptedKinds { get; } =
            new[] { ValueKind.Continuous, ValueKind.Categorical, ValueKind.Ordinal };

        public FieldValue Reduce(FieldDescriptor descriptor, IReadOnlyList<FieldContribution> contributions)
        {
            if (contributions.Count == 0)
                throw new FieldReductionException(
                    descriptor.Id, default, Id, "requires at least one contribution", Array.Empty<string>());
            var c = contributions[0];
            return new FieldValue(descriptor.Id, c.Subject, c.Tick, c.Value, c.Origin, contributions.Count);
        }
    }
}
#endif