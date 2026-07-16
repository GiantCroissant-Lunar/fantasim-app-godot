using System.Collections.Generic;
using Godot;

namespace FantaSim.App.Presentation;

/// <summary>
/// D1 composition: assembles the mantle-interior LAYER view's Node3D tree. The mantle is a
/// selectable layer of the world stack — when active, the presentation composes the M-A interior
/// (core sphere + four anomaly isosurfaces, field method unchanged) with the M-B crust as
/// SEPARATED THICK SLABS (detached but still reading as a sphere, per the Sketchfab exploded-plates
/// reference). The separated slabs — NOT a ghost shell — are the surface reference frame.
/// </summary>
/// <remarks>
/// <para>This helper exists as a focused class (the binder is ~2,400 LOC and flagged a hazard) so
/// the composed-view assembly lives in one testable place. The binder supplies the pre-built piece
/// nodes (it owns the cached slab DTO state and the isosurface material singletons); this class
/// owns the composition contract: what belongs in the mantle-interior tree (core + isosurfaces +
/// slabs) and, just as importantly, what does NOT (no ghost shell — that is the x-ray path's
/// framing device, explicitly rejected by D1 in favor of the separated slabs).</para>
///
/// <para>The root is scaled <c>x2</c> to match the house globe scale (<c>PlateSurfaceRenderer</c>,
/// <c>BuildCutawayFaceSector</c>) so the composed tree aligns with the regular plate surface and
/// cutaway nodes. All piece nodes are parented under this root; the binder parents the root under
/// <c>PlanetBody</c>.</para>
/// </remarks>
internal static class MantleInteriorViewComposer
{
    // One entry per non-empty isosurface mesh. The binder builds the MeshInstance3D (reusing
    // BuildIsosurfaceNode + the cold/warm inner/outer material singletons); this helper just
    // parents them in deterministic draw order (opaque inner cores first, translucent outer halos
    // last) so the transparent pipeline sorts correctly against the core sphere.
    public sealed record IsosurfaceEntry(MeshInstance3D Node, int RenderPriority);

    /// <summary>
    /// Assembles the mantle-interior LAYER view root from the three D1 piece groups. Returns a
    /// Node3D named "MantleInteriorLayer" scaled to the house globe, with children parented in
    /// draw order: core sphere, opaque isosurface inner cores (low priority), separated crust slabs,
    /// translucent isosurface outer halos (high priority). The separated slab root is owned by this
    /// composition (the binder must NOT also parent it). No ghost shell is added.
    /// </summary>
    public static Node3D Compose(
        MeshInstance3D coreSphere,
        IReadOnlyList<IsosurfaceEntry> isosurfaces,
        Node3D separatedSlabRoot)
    {
        var root = new Node3D { Name = "MantleInteriorLayer", Scale = Vector3.One * 2.0f };

        // 1. Core sphere backdrop (profile-driven radius — D3).
        root.AddChild(coreSphere);

        // 2. Opaque inner isosurface cores first (drawn in the opaque pass).
        foreach (var entry in isosurfaces)
        {
            if (entry.RenderPriority <= 1)
                root.AddChild(entry.Node);
        }

        // 3. Separated crust slabs — the D1 surface reference frame (NOT a ghost shell).
        root.AddChild(separatedSlabRoot);

        // 4. Translucent outer isosurface halos last (explicit priority, drawn after opaques).
        foreach (var entry in isosurfaces)
        {
            if (entry.RenderPriority > 1)
                root.AddChild(entry.Node);
        }

        return root;
    }
}
