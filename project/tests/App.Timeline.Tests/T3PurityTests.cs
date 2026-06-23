using System.Reflection;
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
}
