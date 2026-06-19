#if USE_PROJECT_REFERENCES
using System.Collections.Generic;
using FantaSim.World.Fields;
using TimeDete.Time.Primitives;

namespace FantaSim.App.Ecs.Fields;

/// <summary>
/// Identifies which <see cref="SubjectRef"/> a cell-entity represents in the field value plane.
/// Attached once at cell-entity creation; the <see cref="ReduceFieldsSystem"/> reads it to label
/// every contribution on this entity with the entity's subject (so contributors do not repeat it).
/// </summary>
internal readonly record struct FieldSubject(SubjectRef Subject);

/// <summary>
/// Per-cell buffer of pending <see cref="FieldContribution"/>s written by contributor systems
/// during a tick. <see cref="ReduceFieldsSystem"/> consumes the buffer, reduces per
/// (Field, Subject, Tick), writes <see cref="ResolvedFields"/>, and clears the list in place.
/// The list is owned by the entity; reuse the same instance across ticks to avoid per-tick alloc.
/// </summary>
internal readonly record struct FieldContributionBuffer(List<FieldContribution> Contributions);

/// <summary>
/// Per-cell resolved field values keyed by <see cref="FieldId"/>. <see cref="ReduceFieldsSystem"/>
/// overwrites the entry for a field with the latest reduced <see cref="FieldValue"/>. One entry
/// per field; latest tick wins. Read by projection/consumer systems after the reduce phase.
/// </summary>
internal sealed record ResolvedFields(Dictionary<FieldId, FieldValue> ByField)
{
    public ResolvedFields() : this(new Dictionary<FieldId, FieldValue>()) { }
}
#endif