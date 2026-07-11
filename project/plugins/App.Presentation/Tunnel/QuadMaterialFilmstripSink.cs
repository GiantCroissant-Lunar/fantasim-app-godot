using FantaSim.App.Timeline.Seam;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>3D filmstrip sink (plan Task 9): the corridor's texture quad, wired through the same
/// IFilmstripFrameSink seam TextureRectFilmstripSink implements for the 2D face (Task 5). Blits
/// the fetched ImageTexture onto the quad's own StandardMaterial3D.AlbedoTexture -- the cache/
/// queue/ALC-discipline machinery in FilmstripPreviewController is otherwise untouched.
/// vault/plans/2026-07-11-tunnel-slice1-plan.md.</summary>
internal sealed class QuadMaterialFilmstripSink : IFilmstripFrameSink
{
    private readonly MeshInstance3D _owner;
    private readonly StandardMaterial3D _material;

    public QuadMaterialFilmstripSink(MeshInstance3D owner, StandardMaterial3D material)
    {
        _owner = owner;
        _material = material;
    }

    public bool IsAlive => GodotObject.IsInstanceValid(_owner) && _owner.IsInsideTree();
    public void SetTexture(ImageTexture texture) => _material.AlbedoTexture = texture;
}
