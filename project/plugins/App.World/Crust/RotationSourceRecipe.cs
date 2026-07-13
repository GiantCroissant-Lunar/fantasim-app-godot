namespace FantaSim.App.World.Crust;

/// <summary>The plate-rotation source selected by the <c>world.options.rotationSource</c> seam.</summary>
internal enum RotationSourceKind
{
    /// <summary>
    /// Default: per-plate Euler poles come from the generated onset roster (unchanged behavior).
    /// </summary>
    Generated,

    /// <summary>
    /// Per-plate rotations come from an imported GPlates <c>.rot</c> model parsed by the engine's
    /// <c>RotParser</c> and interpolated per tick.
    /// </summary>
    Imported,
}

/// <summary>
/// Recipe mirroring <c>CrustPatchRecipe</c>: captures the rotation-source selection from
/// <c>world.options.rotationSource</c>. When <see cref="Kind"/> is <see cref="RotationSourceKind.Imported"/>,
/// <see cref="RotText"/> carries the inline <c>.rot</c> provenance input into the import coordinator.
/// After prepare/plate/bind succeeds, committed semantic events and their bound cursor are playback
/// authority; this raw recipe is not retained as a second playback path.
/// </summary>
internal sealed record RotationSourceRecipe(
    RotationSourceKind Kind,
    string? RotText,
    string SourceName)
{
    public static RotationSourceRecipe Default { get; } =
        new(RotationSourceKind.Generated, RotText: null, SourceName: "generated");
}
