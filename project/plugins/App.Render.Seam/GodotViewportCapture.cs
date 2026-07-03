using Godot;

namespace FantaSim.App.Render.Seam;

/// <summary>
/// Resident Godot <see cref="IViewportCapture"/>: captures the main window viewport's rendered
/// image to a PNG. Runs on the Godot main thread (the command service's IMainThreadDispatcher
/// marshals handler invocations onto the main loop; see RemoteBridgeNode._Process). Uses the
/// documented Godot 4 capture path: <c>GetViewport().GetTexture().GetImage().SavePng(path)</c>.
/// Sources:
/// https://docs.godotengine.org/en/4.7/classes/class_viewport.html
/// https://docs.godotengine.org/en/4.7/tutorials/rendering/viewports.html
/// </summary>
public sealed partial class GodotViewportCapture : Node, FantaSim.App.Render.IViewportCapture
{
    public (int Width, int Height)? CaptureAndSavePng(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var viewport = GetViewport();
        if (viewport is null)
            return null;

        var texture = viewport.GetTexture();
        if (texture is null)
            return null;

        // get_image() may return null for invalid textures (headless / unrendered viewport).
        var image = texture.GetImage();
        if (image is null)
            return null;

        image.SavePng(absolutePath);
        return (image.GetWidth(), image.GetHeight());
    }
}