using Godot;

namespace FantaSim.App.Timeline.Seam;

/// <summary>Where a fetched filmstrip texture lands. TextureRectFilmstripSink is the 2D adapter
/// (unchanged behavior); App.Presentation's QuadMaterialFilmstripSink (tunnel, 3D) is the second
/// implementation this seam exists for -- spec §4.3's "smallest seam that lets the controller feed
/// both sinks." vault/plans/2026-07-11-tunnel-slice1-plan.md.</summary>
internal interface IFilmstripFrameSink
{
    bool IsAlive { get; }
    void SetTexture(ImageTexture texture);
}

internal sealed class TextureRectFilmstripSink : IFilmstripFrameSink
{
    private readonly TextureRect _textureRect;
    public TextureRectFilmstripSink(TextureRect textureRect) => _textureRect = textureRect;
    public bool IsAlive => GodotObject.IsInstanceValid(_textureRect) && _textureRect.IsInsideTree();
    public void SetTexture(ImageTexture texture) => _textureRect.Texture = texture;
}
