using System.Collections.Generic;
using System.Linq;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Composes a layer stack's field producer->consumer DAG.
/// Stack order = opinion strength: a layer added LATER (higher index) is STRONGER
/// and wins when multiple layers produce the same field (USD sublayer-strength analogy).
/// <para>
/// This composer is PURE: <see cref="Compose"/> has no side effects and does NOT throw
/// for declaration problems; it returns them all in <see cref="FieldCompositionResult.Errors"/>
/// so the caller sees every problem at once.
/// </para>
/// </summary>
public sealed class FieldComposer
{
    private readonly Dictionary<FieldId, FieldDescriptor> _declaredFields = new();
    private readonly List<LayerFieldBinding> _layers = new();

    /// <summary>
    /// Declare a field. Idempotent by FieldId; a later DeclareField with the same id
    /// replaces the prior descriptor.
    /// </summary>
    public void DeclareField(FieldDescriptor descriptor)
    {
        _declaredFields[descriptor.Id] = descriptor;
    }

    /// <summary>
    /// Append a layer to the stack. STACK ORDER = OPINION STRENGTH: a layer added LATER
    /// (higher index) is STRONGER and wins when multiple layers produce the same field.
    /// </summary>
    public void AddLayer(LayerFieldBinding binding)
    {
        _layers.Add(binding);
    }

    /// <summary>
    /// Resolve the whole stack. PURE -- no side effects, no throws for declaration problems.
    /// </summary>
    public FieldCompositionResult Compose()
    {
        var errors = new List<FieldCompositionError>();
        var winningProducers = new Dictionary<FieldId, LayerId>();
        var unsatisfiedOptional = new List<FieldId>();

        // --- 0. Duplicate-layer guard. Two layers sharing a LayerId is a manifest error and
        // would make producer-by-LayerId resolution ambiguous; surface it loudly. ---
        var seenLayerIds = new HashSet<LayerId>();
        foreach (var layer in _layers)
        {
            if (!seenLayerIds.Add(layer.Layer))
                errors.Add(new FieldCompositionError(
                    FieldCompositionErrorKind.DuplicateLayer,
                    $"Layer '{layer.Layer}' is declared more than once in the stack."));
        }

        // --- 1. Collect all field IDs referenced by layers, check for unknowns ---
        var allReferencedFieldIds = new HashSet<FieldId>();

        for (int i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            foreach (var fieldId in layer.Produces)
            {
                allReferencedFieldIds.Add(fieldId);
                if (!_declaredFields.ContainsKey(fieldId))
                {
                    errors.Add(new FieldCompositionError(
                        FieldCompositionErrorKind.UnknownField,
                        $"Layer '{layer.Layer}' produces undeclared field '{fieldId}'."));
                }
            }

            foreach (var consumption in layer.Consumes)
            {
                allReferencedFieldIds.Add(consumption.Field);
                if (!_declaredFields.ContainsKey(consumption.Field))
                {
                    errors.Add(new FieldCompositionError(
                        FieldCompositionErrorKind.UnknownField,
                        $"Layer '{layer.Layer}' consumes undeclared field '{consumption.Field}'."));
                }
            }
        }

        // --- 2. Determine winning producers (highest stack index wins) ---
        // Also build a map: field -> list of layer indices that produce it
        var fieldProducers = new Dictionary<FieldId, List<int>>();
        for (int i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            foreach (var fieldId in layer.Produces)
            {
                if (!fieldProducers.TryGetValue(fieldId, out var producerList))
                {
                    producerList = new List<int>();
                    fieldProducers[fieldId] = producerList;
                }
                producerList.Add(i);
            }
        }

        // Winning producer = highest stack index for each field. Keep the winner's INDEX (not
        // just its LayerId) so edge-wiring below never re-resolves LayerId -> index, which is
        // ambiguous when a LayerId is duplicated.
        var winningProducerIndex = new Dictionary<FieldId, int>();
        foreach (var kvp in fieldProducers)
        {
            // Highest index = strongest opinion.
            var winnerIndex = kvp.Value[^1];
            winningProducerIndex[kvp.Key] = winnerIndex;
            winningProducers[kvp.Key] = _layers[winnerIndex].Layer;
        }

        // --- 3. Check for unresolved required fields and unsatisfied optional fields ---
        for (int i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            foreach (var consumption in layer.Consumes)
            {
                // Self-consumption satisfied by self-production: skip
                if (layer.Produces.Contains(consumption.Field))
                    continue;

                bool hasProducer = winningProducers.ContainsKey(consumption.Field);

                if (!hasProducer)
                {
                    if (consumption.Required)
                    {
                        errors.Add(new FieldCompositionError(
                            FieldCompositionErrorKind.UnresolvedRequiredField,
                            $"Layer '{layer.Layer}' requires field '{consumption.Field}' which has no producer in the stack."));
                    }
                    else if (consumption.Default is null)
                    {
                        // Optional, no producer, no fallback -> unsatisfied (runtime gets nothing).
                        unsatisfiedOptional.Add(consumption.Field);
                    }
                    // else: optional with a declared default -> satisfied by fallback, not reported.
                }
            }
        }

        // --- 4. Build the dependency graph: an edge producer -> consumer exists iff the
        // consumer consumes a field whose WINNING producer is a DIFFERENT layer
        // (self-production creates no edge). Multiple shared fields between the same pair
        // collapse to ONE edge so in-degrees stay correct. ---
        var adjacency = new Dictionary<int, List<int>>();
        var inDegree = new int[_layers.Count];
        for (int i = 0; i < _layers.Count; i++)
            adjacency[i] = new List<int>();

        var uniqueEdges = new HashSet<(int from, int to)>();
        for (int consumerIdx = 0; consumerIdx < _layers.Count; consumerIdx++)
        {
            var consumer = _layers[consumerIdx];
            foreach (var consumption in consumer.Consumes)
            {
                // Self-production satisfies self-consumption: no dependency edge.
                if (consumer.Produces.Contains(consumption.Field))
                    continue;

                if (winningProducerIndex.TryGetValue(consumption.Field, out var producerIdx)
                    && producerIdx != consumerIdx)
                {
                    uniqueEdges.Add((producerIdx, consumerIdx));
                }
            }
        }

        foreach (var (from, to) in uniqueEdges)
        {
            adjacency[from].Add(to);
            inDegree[to]++;
        }

        // --- 5. Topological sort (Kahn's algorithm, deterministic: break ties by ascending stack index) ---
        var executionOrder = new List<LayerId>();
        var queue = new SortedSet<int>(); // SortedSet gives us ascending stack index tie-breaking

        for (int i = 0; i < _layers.Count; i++)
        {
            if (inDegree[i] == 0)
                queue.Add(i);
        }

        while (queue.Count > 0)
        {
            var current = queue.Min!;
            queue.Remove(current);
            executionOrder.Add(_layers[current].Layer);

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Add(neighbor);
            }
        }

        // --- 6. Detect cycles ---
        if (executionOrder.Count < _layers.Count)
        {
            errors.Add(new FieldCompositionError(
                FieldCompositionErrorKind.Cycle,
                "The producer->consumer dependency graph contains a cycle."));
        }

        return new FieldCompositionResult(
            executionOrder,
            winningProducers,
            unsatisfiedOptional,
            errors);
    }
}
