namespace FantaSim.App.Render;

/// <summary>
/// Godot-facing capture abstraction implemented by the T4 seam. The handler runs on the
/// Godot main thread; the implementation calls <c>GetViewport().GetTexture().GetImage()</c>
/// and <c>Image.SavePng(path)</c>. Returns the captured pixel dimensions.
/// </summary>
public interface IViewportCapture
{
    /// <summary>
    /// Captures the main viewport image and saves it as a PNG at <paramref name="absolutePath"/>.
    /// Returns the image dimensions, or null when there is no viewport/texture (e.g. headless).
    /// </summary>
    (int Width, int Height)? CaptureAndSavePng(string absolutePath);
}