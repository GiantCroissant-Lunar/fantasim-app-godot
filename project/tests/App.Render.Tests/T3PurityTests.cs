using System.Reflection;
using Xunit;

namespace App.Render.Tests;

public class T3PurityTests
{
    [Fact]
    public void T3_Assembly_HasNoGodotReference()
    {
        // The T3 render assembly must be pure C# (no GodotSharp reference) so the collectible
        // ALC unloads cleanly. Only the T4 seam (App.Render.Seam) may reference Godot.
        var asm = typeof(FantaSim.App.Render.ScreenshotRequest).Assembly;
        var referenced = asm.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, r => r.Name == "GodotSharp");
    }
}