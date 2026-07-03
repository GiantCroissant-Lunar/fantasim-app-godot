using System.Globalization;
using Xunit;

namespace App.Render.Tests;

public class ScreenshotRequestTests
{
    // ---- ParsePath ----

    [Fact]
    public void ParsePath_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(FantaSim.App.Render.ScreenshotRequest.ParsePath(null));
        Assert.Null(FantaSim.App.Render.ScreenshotRequest.ParsePath(""));
        Assert.Null(FantaSim.App.Render.ScreenshotRequest.ParsePath("   "));
    }

    [Fact]
    public void ParsePath_NoPathKey_ReturnsNull()
    {
        Assert.Null(FantaSim.App.Render.ScreenshotRequest.ParsePath("{}"));
        Assert.Null(FantaSim.App.Render.ScreenshotRequest.ParsePath("{\"other\":1}"));
    }

    [Fact]
    public void ParsePath_WithPath_ReturnsPath()
    {
        Assert.Equal(
            "/tmp/shot.png",
            FantaSim.App.Render.ScreenshotRequest.ParsePath("{\"path\":\"/tmp/shot.png\"}"));
    }

    [Fact]
    public void ParsePath_UserPath_ReturnedAsIs()
    {
        // user:// resolution is the seam's job; ParsePath only extracts the string.
        Assert.Equal(
            "user://screenshots/x.png",
            FantaSim.App.Render.ScreenshotRequest.ParsePath("{\"path\":\"user://screenshots/x.png\"}"));
    }

    [Fact]
    public void ParsePath_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.ScreenshotRequest.ParsePath("{\"path\":\"\"}"));
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.ScreenshotRequest.ParsePath("{\"path\":\"   \"}"));
    }

    [Fact]
    public void ParsePath_NotAnObject_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.ScreenshotRequest.ParsePath("\"not an object\""));
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.ScreenshotRequest.ParsePath("[1,2,3]"));
    }

    // ---- BuildDefaultPath ----

    [Fact]
    public void BuildDefaultPath_UsesUtcTimestampFormat()
    {
        var utc = new DateTimeOffset(2026, 7, 3, 14, 5, 9, TimeSpan.Zero);
        var path = FantaSim.App.Render.ScreenshotRequest.BuildDefaultPath(utc);
        Assert.Equal("user://screenshots/20260703-140509.png", path);
    }

    [Fact]
    public void BuildDefaultPath_CustomDirectory()
    {
        var utc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var path = FantaSim.App.Render.ScreenshotRequest.BuildDefaultPath(utc, "/tmp/shots");
        Assert.Equal("/tmp/shots/20260102-030405.png", path);
    }

    [Fact]
    public void BuildDefaultPath_EmptyDirectory_FallsBackToDefault()
    {
        var utc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Assert.Equal(
            "user://screenshots/20260102-030405.png",
            FantaSim.App.Render.ScreenshotRequest.BuildDefaultPath(utc, ""));
        Assert.Equal(
            "user://screenshots/20260102-030405.png",
            FantaSim.App.Render.ScreenshotRequest.BuildDefaultPath(utc, "   "));
        Assert.Equal(
            "user://screenshots/20260102-030405.png",
            FantaSim.App.Render.ScreenshotRequest.BuildDefaultPath(utc, null));
    }

    [Fact]
    public void BuildDefaultPath_TimestampIsInvariant()
    {
        // The stamp must not depend on the current culture (no locale-specific separators).
        var utc = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var path = FantaSim.App.Render.ScreenshotRequest.BuildDefaultPath(utc);
        var stamp = path.Split('/').Last().Replace(".png", "");
        Assert.Equal("20261231-235959", stamp);
        Assert.True(DateTime.TryParseExact(
            stamp, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _));
    }

    // ---- ResolveAbsolutePath ----

    [Fact]
    public void ResolveAbsolutePath_UserPath_Globalized()
    {
        var calls = new List<string>();
        Func<string, string> globalize = p =>
        {
            calls.Add(p);
            return "/Users/fantasim/Library/Application Support/Godot/app_userdata/screenshots/x.png";
        };

        var result = FantaSim.App.Render.ScreenshotRequest.ResolveAbsolutePath(
            "user://screenshots/x.png", globalize);

        Assert.Equal(
            "/Users/fantasim/Library/Application Support/Godot/app_userdata/screenshots/x.png",
            result);
        Assert.Single(calls);
        Assert.Equal("user://screenshots/x.png", calls[0]);
    }

    [Fact]
    public void ResolveAbsolutePath_AbsolutePath_PassedThroughUnchanged()
    {
        Func<string, string> globalize = _ => "SHOULD-NOT-BE-CALLED";
        var result = FantaSim.App.Render.ScreenshotRequest.ResolveAbsolutePath(
            "/tmp/shot.png", globalize);
        Assert.Equal("/tmp/shot.png", result);
    }

    [Fact]
    public void ResolveAbsolutePath_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FantaSim.App.Render.ScreenshotRequest.ResolveAbsolutePath(null!, _ => "x"));
        Assert.Throws<ArgumentNullException>(() =>
            FantaSim.App.Render.ScreenshotRequest.ResolveAbsolutePath("/x", null!));
    }

    // ---- EnsureDirectoryExists ----

    [Fact]
    public void EnsureDirectoryExists_CreatesNestedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "render-screenshot-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var filePath = Path.Combine(root, "nested", "deeper", "shot.png");
            var dir = FantaSim.App.Render.ScreenshotRequest.EnsureDirectoryExists(filePath);
            Assert.True(Directory.Exists(dir));
            Assert.EndsWith(Path.Combine("nested", "deeper"), dir);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnsureDirectoryExists_ExistingDirectory_NoThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "render-screenshot-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(root);
            var filePath = Path.Combine(root, "shot.png");
            var dir = FantaSim.App.Render.ScreenshotRequest.EnsureDirectoryExists(filePath);
            Assert.Equal(root, dir);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnsureDirectoryExists_FileBlockingDirectory_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "render-screenshot-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(root);
            // Create a FILE whose name is the would-be directory.
            var blockingFile = Path.Combine(root, "sub");
            File.WriteAllText(blockingFile, "block");
            var filePath = Path.Combine(root, "sub", "shot.png");
            Assert.Throws<InvalidOperationException>(() =>
                FantaSim.App.Render.ScreenshotRequest.EnsureDirectoryExists(filePath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}