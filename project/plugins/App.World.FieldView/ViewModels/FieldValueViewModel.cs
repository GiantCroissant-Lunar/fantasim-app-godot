namespace FantaSim.App.World.FieldView.ViewModels;

using FantaSim.App.World.Dto;

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
    /// Typed field descriptor from <c>WorldFieldValues.FieldValues</c> (unit, kind, reducer).
    /// Null when the field id is not present in the world's catalog.
    /// </summary>
    public WorldFieldDescriptorDto? Value { get; init; }

    /// <summary>Scalar value from <c>WorldScalarFieldValues</c>; null when the field is not scalar.</summary>
    public float? Scalar { get; init; }

    /// <summary>Tick/sequence at which this view was refreshed. Mirrors the latest generation change.</summary>
    public long RefreshedTick { get; init; }
}