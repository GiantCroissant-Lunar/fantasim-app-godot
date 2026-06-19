namespace FantaSim.App.World.Projection.ViewModels;

/// <summary>
/// Immutable view model projected from <c>WorldFieldValues</c>/<c>WorldScalarFieldValues</c>.
/// One instance per known field id; the projection refreshes these on every generation
/// change and replaces the entry inside the <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>.
/// </summary>
public sealed record FieldValueViewModel
{
    /// <summary>Field id as reported by the world T1 contract.</summary>
    public required string FieldId { get; init; }

    /// <summary>
    /// Raw value payload from <c>WorldFieldValues.FieldValues</c>. Typed <c>object</c> because the
    /// app-side DTO keeps field values boxed (<c>IReadOnlyDictionary&lt;string, object&gt;</c>); the
    /// projection does not invent a tighter type than the T1 contract exposes.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>Scalar value from <c>WorldScalarFieldValues</c>; null when the field is not scalar.</summary>
    public float? Scalar { get; init; }

    /// <summary>Tick/sequence at which this view was projected. Mirrors the latest generation change.</summary>
    public long ProjectedTick { get; init; }
}