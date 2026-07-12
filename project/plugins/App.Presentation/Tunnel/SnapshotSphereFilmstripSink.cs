using FantaSim.App.Timeline.Seam;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>Applies proven real preview products to one sphere-local material. All other source
/// kinds clear the texture and keep the explicit unavailable marker visible.</summary>
internal sealed class SnapshotSphereFilmstripSink : IFilmstripFrameSink
{
    private readonly MeshInstance3D _sphere;
    private readonly StandardMaterial3D _material;
    private readonly Node3D _unavailable;

    internal SnapshotSphereFilmstripSink(
        MeshInstance3D sphere,
        StandardMaterial3D material,
        Node3D unavailable)
    {
        _sphere = sphere;
        _material = material;
        _unavailable = unavailable;
        _material.AlbedoTexture = null;
        _sphere.Visible = false;
        _unavailable.Visible = true;
    }

    public bool IsAlive
        => GodotObject.IsInstanceValid(_sphere)
           && GodotObject.IsInstanceValid(_unavailable)
           && _sphere.IsInsideTree();

    public void SetFrame(FilmstripFramePayload frame)
    {
        var state = TunnelSnapshotSourcePolicy.StateFor(frame.Metadata.SourceKind);
        _material.AlbedoTexture = state.SphereVisible ? frame.Texture : null;
        _sphere.Visible = state.SphereVisible;
        _unavailable.Visible = state.UnavailableVisible;
    }
}
