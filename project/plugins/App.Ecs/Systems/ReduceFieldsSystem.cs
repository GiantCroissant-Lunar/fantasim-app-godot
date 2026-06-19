#if USE_PROJECT_REFERENCES
using System.Collections.Generic;
using Arch.Core;
using FantaSim.App.Ecs.Fields;
using FantaSim.World.Fields;
using TimeDete.Time.Primitives;
using UnifyECS;
// Alias: with sibling fantasim-world refs, the unqualified `World` resolves to the
// `FantaSim.World` namespace; alias the Arch world type to keep the IArchSystem signature clean.
using ArchWorld = Arch.Core.World;

namespace FantaSim.App.Ecs.Systems;

/// <summary>
/// ECS field-reduction system (app T3). Runs after contributor systems have written
/// <see cref="FieldContributionBuffer"/> entries on cell-entities. For each cell-entity:
/// <list type="bullet">
/// <item>Group pending contributions by <c>(FieldId, Subject, CanonicalTick)</c> — the subject
///       comes from the entity's <see cref="FieldSubject"/> component.</item>
/// <item>Canonically sort each group by <c>(Tick, ProducerId)</c> so order-dependent reducers
///       (last-wins, supplied-overrides-derived) are deterministic regardless of contributor
///       write order.</item>
/// <item>Look up the <see cref="FieldDescriptor"/> via <see cref="IFieldCatalog"/> and the
///       <see cref="IFieldReducer"/> via <see cref="IFieldReducerRegistry"/>; call
///       <c>Reduce</c> on the ordered group.</item>
/// <item>Write the resulting <see cref="FieldValue"/> into <see cref="ResolvedFields"/>,
///       keyed by <c>FieldId</c> (latest tick wins).</item>
/// <item>Clear the contribution buffer in place so the next tick starts clean.</item>
/// </list>
/// The reducer and catalog are pure fantasim-world types; this system is the app-side
/// FieldValueResolver assembly point. World-lib types never leak through App.Ecs T1.
/// </summary>
internal sealed class ReduceFieldsSystem : IUnifySystem, IArchSystem
{
    private readonly IFieldCatalog _catalog;
    private readonly IFieldReducerRegistry _reducers;

    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<FieldSubject, FieldContributionBuffer, ResolvedFields>();

    public ReduceFieldsSystem(IFieldCatalog catalog, IFieldReducerRegistry reducers)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _reducers = reducers ?? throw new ArgumentNullException(nameof(reducers));
    }

    public void Execute(ArchWorld world, float deltaTime)
    {
        // Per-entity accumulator: field id -> (latest tick, ordered contributions for that tick).
        // Reused across entities to avoid per-entity allocation.
        var perField = new Dictionary<FieldId, GroupAccum>();

        world.Query(in _query, (Arch.Core.Entity entity, ref FieldSubject subject, ref FieldContributionBuffer buffer, ref ResolvedFields resolved) =>
        {
            var contributions = buffer.Contributions;
            if (contributions.Count == 0) return;

            perField.Clear();
            foreach (var c in contributions)
            {
                if (!perField.TryGetValue(c.Field, out var acc))
                {
                    acc = new GroupAccum();
                    perField[c.Field] = acc;
                }
                // Group by (Field, Subject, Tick). The entity owns one subject; contributions
                // whose subject mismatches the entity's are still grouped by their own subject
                // to honor the (Field, Subject, Tick) contract literally.
                var key = new GroupKey(c.Subject, c.Tick);
                if (!acc.ByGroup.TryGetValue(key, out var list))
                {
                    list = new List<FieldContribution>();
                    acc.ByGroup[key] = list;
                }
                list.Add(c);
            }

            foreach (var (fieldId, acc) in perField)
            {
                if (!_catalog.TryGet(fieldId, out var descriptor))
                    continue; // Unknown field id: skip rather than throw mid-tick.
                if (!_reducers.TryGet(descriptor.Reducer, out var reducer))
                    continue; // Unregistered reducer: skip; CatalogValidator catches this at startup.

                // Reduce each (subject, tick) group and keep the latest tick's value for the field.
                FieldValue? latest = null;
                foreach (var (_, list) in acc.ByGroup)
                {
                    list.Sort(CompareCanonically);
                    var value = reducer.Reduce(descriptor, list);
                    if (latest is not { } current || value.Tick > current.Tick)
                        latest = value;
                }
                if (latest is { } resolvedValue)
                    resolved.ByField[fieldId] = resolvedValue;
            }

            // Clear the consumed buffer in place; the list instance is reused next tick.
            contributions.Clear();
        });
    }

    /// <summary>Canonical order: (Tick ascending, then ProducerId ordinal ascending).</summary>
    private static int CompareCanonically(FieldContribution a, FieldContribution b)
    {
        var tickCmp = a.Tick.CompareTo(b.Tick);
        if (tickCmp != 0) return tickCmp;
        return string.Compare(a.ProducerId, b.ProducerId, StringComparison.Ordinal);
    }

    private readonly record struct GroupKey(SubjectRef Subject, CanonicalTick Tick);

    private sealed class GroupAccum
    {
        public Dictionary<GroupKey, List<FieldContribution>> ByGroup { get; } = new();
    }
}
#endif