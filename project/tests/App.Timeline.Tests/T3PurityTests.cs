using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace App.Timeline.Tests;

public class T3PurityTests
{
    [Fact]
    public void T3_Assembly_HasNoGodotReference()
    {
        // The T3 plugin assembly must be pure C# (no GodotSharp reference) so the collectible
        // ALC unloads cleanly. Only the T4 seam (App.Timeline.Seam) may reference Godot.
        var asm = typeof(FantaSim.App.Timeline.TimelineModel).Assembly;
        var referenced = asm.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, r => r.Name == "GodotSharp");
    }

    [Fact]
    public void T3_Assembly_IsNotGodotDerived()
    {
        // TimelineModel must NOT extend Godot.Node / Control / Resource.
        var modelType = typeof(FantaSim.App.Timeline.TimelineModel);
        Assert.False(modelType.IsSubclassOf(typeof(Godot.GodotObject))
            || modelType.IsSubclassOf(typeof(Godot.Node))
            || modelType.IsSubclassOf(typeof(Godot.Resource)));
    }

    [Fact]
    public void Tunnel_inner_and_wall_presentation_have_no_timeline_authority()
    {
        var tunnelDirectory = ProjectPath("project/plugins/App.Presentation/Tunnel");
        var occurrences = Directory
            .GetFiles(tunnelDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    @"\bPushTick\s*\(")
                .Select(match => (Path: path, match.Index)))
            .ToArray();

        var only = Assert.Single(occurrences);
        Assert.EndsWith("TunnelPresentationBinder.Input.cs", only.Path);

        var inputSource = File.ReadAllText(only.Path);
        var owningMethod = inputSource.LastIndexOf(
            "private void ApplyOuterScrubAction",
            only.Index,
            StringComparison.Ordinal);
        Assert.True(owningMethod >= 0,
            "The tunnel's sole PushTick call must remain inside ApplyOuterScrubAction.");
    }

    private static string ProjectPath(
        string relativePath,
        [CallerFilePath] string testSourcePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testSourcePath)
            ?? throw new InvalidOperationException("Test source directory is unavailable.");
        return Path.GetFullPath(Path.Combine(testDirectory, "..", "..", "..", relativePath));
    }
}
