using System.Globalization;
using System.Text.Json.Nodes;

namespace FantaSim.App.Render;

/// <summary>
/// Godot-free payload parsing and path resolution for the <c>render.screenshot</c> command.
/// The T4 seam supplies the <see cref="IViewportCapture"/> (Godot viewport) and the
/// <c>user://</c> globalizer; this type owns the pure-C# logic so it is unit-testable
/// without Godot.
/// </summary>
public static class ScreenshotRequest
{
    /// <summary>
    /// Default screenshot directory under the Godot user data root.
    /// </summary>
    public const string DefaultUserDirectory = "user://screenshots";

    /// <summary>
    /// Parses the optional <c>{"path":"..."}</c> payload. Returns null when no path is
    /// supplied (caller falls back to the timestamped default). Throws <see cref="ArgumentException"/>
    /// when the payload is present but not a JSON object or the path is empty.
    /// </summary>
    public static string? ParsePath(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("render.screenshot payload must be a JSON object.");

        var pathNode = payload["path"];
        if (pathNode is null)
            return null;

        var path = pathNode.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("render.screenshot 'path' must not be empty.");

        return path;
    }

    /// <summary>
    /// Builds the default timestamped filename (<c>yyyyMMdd-HHmmss</c>) under
    /// <paramref name="directory"/>. <paramref name="directory"/> defaults to
    /// <see cref="DefaultUserDirectory"/> when null/empty.
    /// </summary>
    public static string BuildDefaultPath(DateTimeOffset utcNow, string? directory = null)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? DefaultUserDirectory : directory!;
        var stamp = utcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return $"{dir}/{stamp}.png";
    }

    /// <summary>
    /// Resolves a <c>user://</c>-prefixed path to an absolute OS path via the supplied
    /// globalizer (the T4 seam binds this to <c>ProjectSettings.GlobalizePath</c>).
    /// Absolute filesystem paths are returned unchanged. Relative paths are treated as
    /// already-absolute-on-this-host (callers pass absolute paths); we do not invent a CWD.
    /// </summary>
    public static string ResolveAbsolutePath(string path, Func<string, string> globalizeUserPath)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(globalizeUserPath);

        if (path.StartsWith("user://", StringComparison.Ordinal))
            return globalizeUserPath(path);

        return path;
    }

    /// <summary>
    /// Ensures the directory portion of <paramref name="absolutePath"/> exists, creating it
    /// if needed. Returns the directory path. Throws <see cref="InvalidOperationException"/>
    /// when the directory cannot be created or is not a directory.
    /// </summary>
    public static string EnsureDirectoryExists(string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        var dir = Path.GetDirectoryName(absolutePath);
        if (string.IsNullOrEmpty(dir))
            return string.Empty;

        if (!Directory.Exists(dir))
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not create screenshot directory '{dir}': {ex.Message}", ex);
            }
        }

        if (!Directory.Exists(dir) || File.Exists(dir))
            throw new InvalidOperationException($"Screenshot target directory '{dir}' is not a directory.");

        return dir;
    }
}