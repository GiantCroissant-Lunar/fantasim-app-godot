using FantaSim.App.Timeline.Seam;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>Applies proven real preview products to one sphere-local material. All other source
/// kinds clear the texture and keep the explicit unavailable marker visible.</summary>
internal sealed class SnapshotSphereFilmstripSink : IFilmstripFrameSink
{
    private readonly MeshInstance3D _sphere;
    private readonly SnapshotSphereMaterial _material;
    private readonly Node3D _unavailable;

    internal SnapshotSphereFilmstripSink(
        MeshInstance3D sphere,
        SnapshotSphereMaterial material,
        Node3D unavailable)
    {
        _sphere = sphere;
        _material = material;
        _unavailable = unavailable;
        _material.SetTexture(null);
        _sphere.Visible = false;
        _unavailable.Visible = true;
    }

    public bool IsAlive
        => GodotObject.IsInstanceValid(_sphere)
           && GodotObject.IsInstanceValid(_unavailable)
           && _material.IsAlive
           && _sphere.IsInsideTree();

    public void SetFrame(FilmstripFramePayload frame)
    {
        var state = TunnelSnapshotSourcePolicy.StateFor(frame.Metadata.SourceKind);
        if (state.SphereVisible)
            _material.SetTexture(frame.Texture);
        else
            _material.SetTexture(null);
        _sphere.Visible = state.SphereVisible;
        _unavailable.Visible = state.UnavailableVisible;
    }
}
