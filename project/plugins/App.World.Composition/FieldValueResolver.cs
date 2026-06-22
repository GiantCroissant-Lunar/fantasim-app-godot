using System;
using System.Collections.Generic;
using System.Linq;

using FantaSim.App.World;   // WorldGlobeGeometry

namespace FantaSim.App.World.Composition;

/// <summary>
/// Resolves every field's per-cell values for one tick by running the composition's
/// ExecutionOrder. PURE (no caching here -- the caller caches per tick). THROWS on any
/// invariant violation (the resident producer set is authored in-repo and must satisfy them).
/// </summary>
public sealed class FieldValueResolver
{
    /// <summary>
    /// Resolve every field's per-cell values for one tick by running the composition's
    /// ExecutionOrder. PURE (no caching here -- the caller caches per tick). THROWS on any
    /// invariant violation (the resident producer set is authored in-repo and must satisfy them).
    /// </summary>
    public WorldFieldValues Resolve(
        FieldCompositionResult composition,
        IReadOnlyList<ILayer> layers,
        WorldGlobeGeometry geometry,
        long tick)
    {
        // 0. Precondition: a public primitive guards its own input -- never resolve an INVALID
        //    composition (the resident path also fail-fasts upstream in ComposeResidentLayers).
        if (!composition.IsValid)
        {
            var problems = string.Join("; ", composition.Errors.Select(e => $"{e.Kind}: {e.Message}"));
            throw new InvalidOperationException(
                $"Cannot resolve field values from an invalid composition ({composition.Errors.Count} error(s)): {problems}");
        }

        // 1. CellKeys = geometry.Cells plate ids in order; cellCount.
        var cellKeys = geometry.Cells.Select(c => c.PlateId).ToList();
        int cellCount = cellKeys.Count;

        // 2. Scalar accumulator + layer lookup by id.
        var scalars = new Dictionary<FieldId, double[]>();
        var layerById = layers.ToDictionary(l => l.Id);

        // 3. DEFAULT MATERIALIZATION: for each layer, for each optional consumption
        //    where no winning producer exists and a Default is declared, broadcast the default.
        foreach (var layer in layers)
        {
            foreach (var consumption in layer.Fields.Consumes)
            {
                if (consumption.Required)
                    continue;

                // Skip if a winning producer covers this field.
                if (composition.WinningProducers.ContainsKey(consumption.Field))
                    continue;

                if (consumption.Default is not null)
                {
                    var defaultValue = Convert.ToDouble(consumption.Default);
                    scalars[consumption.Field] = Enumerable.Range(0, cellCount)
                        .Select(_ => defaultValue).ToArray();
                }
                // Default is null -> genuinely unsatisfied optional -> leave absent.
            }
        }

        // 4. Build an internal IFieldComputeContext over (tick, geometry, cellCount, scalars).
        var context = new ComputeContext(tick, geometry, cellCount, scalars);

        // 5. For each layerId in ExecutionOrder, if it is IFieldProducer, call Produce. A producer
        //    only OWNS (and may store) the fields it WINS; a write to a field it declares but loses
        //    is ignored so the winner's value stands regardless of run order. Reads are limited to
        //    its declared Consumes (+ its own outputs).
        foreach (var layerId in composition.ExecutionOrder)
        {
            var layer = layerById[layerId];
            if (layer is IFieldProducer producer)
            {
                var declaredWrites = producer.Fields.Produces;
                var ownedWrites = declaredWrites
                    .Where(f => composition.WinningProducers.TryGetValue(f, out var winner) && winner.Equals(producer.Id))
                    .ToList();
                var allowedReads = new HashSet<FieldId>(producer.Fields.Consumes.Select(c => c.Field));
                foreach (var owned in ownedWrites) allowedReads.Add(owned);   // may read its own WON output (NOT a lost one -- reading a declared-but-lost field is the winner's value and must be a declared Consume)

                context.BeginProducer(declaredWrites, ownedWrites, allowedReads);
                producer.Produce(context);

                // INVARIANT 2 (declared outputs): every field this producer OWNS was written.
                foreach (var owned in ownedWrites)
                {
                    if (!context.WasWritten(owned))
                    {
                        throw new InvalidOperationException(
                            $"Producer '{layer.Id}' did not write its declared field '{owned}'.");
                    }
                }
            }
        }

        // 6. INVARIANT 1 (producer coverage): every winning field must have produced values.
        foreach (var field in composition.WinningProducers.Keys)
        {
            if (!scalars.ContainsKey(field))
            {
                throw new InvalidOperationException(
                    $"Winning field '{field}' has no produced values (producer layer is not an IFieldProducer or did not write it).");
            }
        }

        // 7. Return the result.
        return new WorldFieldValues(
            tick,
            cellKeys,
            scalars.Select(kv => new WorldScalarFieldValues(kv.Key, kv.Value)).ToList());
    }

    private sealed class ComputeContext : IFieldComputeContext
    {
        private readonly Dictionary<FieldId, double[]> _scalars;
        private readonly HashSet<FieldId> _written = new();
        private IReadOnlyCollection<FieldId> _allowedWrites = System.Array.Empty<FieldId>();
        private IReadOnlyCollection<FieldId> _ownedWrites = System.Array.Empty<FieldId>();
        private HashSet<FieldId> _allowedReads = new();

        public long Tick { get; }
        public WorldGlobeGeometry Geometry { get; }
        public int CellCount { get; }

        public ComputeContext(long tick, WorldGlobeGeometry geometry, int cellCount, Dictionary<FieldId, double[]> scalars)
        {
            Tick = tick;
            Geometry = geometry;
            CellCount = cellCount;
            _scalars = scalars;
        }

        public void BeginProducer(
            IReadOnlyCollection<FieldId> declaredWrites,
            IReadOnlyCollection<FieldId> ownedWrites,
            HashSet<FieldId> allowedReads)
        {
            _allowedWrites = declaredWrites;
            _ownedWrites = ownedWrites;
            _allowedReads = allowedReads;
            _written.Clear();
        }

        public bool WasWritten(FieldId field) => _written.Contains(field);

        public IReadOnlyList<double> GetScalar(FieldId field)
        {
            // INVARIANT (declared reads): a producer may only read fields it declared consuming
            // (plus its own outputs) -- else real data flow would bypass the composition DAG and
            // ExecutionOrder would no longer describe it.
            if (!_allowedReads.Contains(field))
            {
                throw new InvalidOperationException(
                    $"Producer read undeclared field '{field}' (not in its Consumes).");
            }

            // INVARIANT 5 (read fails loud): declared, but not yet produced upstream.
            if (!_scalars.TryGetValue(field, out var values))
            {
                throw new InvalidOperationException(
                    $"Field '{field}' was read before it was produced.");
            }
            return values;
        }

        public void SetScalar(FieldId field, IReadOnlyList<double> perCell)
        {
            // INVARIANT 3 (no undeclared writes).
            if (!_allowedWrites.Contains(field))
            {
                throw new InvalidOperationException(
                    $"Producer wrote undeclared field '{field}'.");
            }

            // INVARIANT 4 (length).
            if (perCell.Count != CellCount)
            {
                throw new InvalidOperationException(
                    $"Field '{field}' has {perCell.Count} values but expected {CellCount} (one per cell).");
            }

            // Only the WINNING producer of a field stores its value; a loser's write of a field it
            // declares but does not win is IGNORED, so the winner's value is authoritative regardless
            // of execution order (composition.WinningProducers).
            if (_ownedWrites.Contains(field))
            {
                _scalars[field] = perCell.ToArray();
                _written.Add(field);
            }
        }
    }
}
