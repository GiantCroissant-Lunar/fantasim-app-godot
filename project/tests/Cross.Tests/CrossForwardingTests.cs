using Xunit;

namespace FantaSim.Cross.Tests;

// Tier-1 interface: only [CrossDelegate] methods are forwarded into the bound surface; InternalOnly stays C#-only.
public interface IPingService
{
    [CrossDelegate] string Ping(string message);
    [CrossDelegate] int Count();
    [CrossDelegate] void Reset();
    void InternalOnly(); // deliberately NOT a cross member - proves the generator forwards only [CrossDelegate]
}

// Plain-C# Tier-3 implementation - no Godot base, ALC-eligible.
public sealed class PingService : IPingService
{
    private int _count;
    public string Ping(string message) { _count++; return $"pong: {message}"; }
    public int Count() => _count;
    public void Reset() => _count = 0;
    public void InternalOnly() { }
}

// Thin resident binder. It can be any resident type - here a plain class; in the scene tree the
// hand-written part could add `: Godot.Node`. The generator emits BindCrossTarget / UnbindCrossTarget /
// IsCrossBound and forwarding Ping / Count / Reset (and omits InternalOnly).
[CrossService(typeof(IPingService))]
public partial class PingCross
{
}

public class CrossForwardingTests
{
    [Fact]
    public void Unbound_surface_reports_not_bound_and_returns_default()
    {
        var cross = new PingCross();

        // Generated members exist (compile-time proof) and the surface is null-safe before binding.
        Assert.False(cross.IsCrossBound);
        Assert.Null(cross.Ping("x")); // default(string) when no target is bound
        Assert.Equal(0, cross.Count()); // default(int)
        cross.Reset();                  // void forwarding is a no-op, must not throw
    }

    [Fact]
    public void Bound_surface_forwards_calls_to_the_target()
    {
        var cross = new PingCross();
        cross.BindCrossTarget(new PingService());

        Assert.True(cross.IsCrossBound);
        Assert.Equal("pong: hello", cross.Ping("hello"));
        Assert.Equal(1, cross.Count());
        cross.Ping("again");
        Assert.Equal(2, cross.Count());
        cross.Reset();
        Assert.Equal(0, cross.Count());
    }

    [Fact]
    public void Rebinding_swaps_the_target_and_unbinding_detaches_it()
    {
        var cross = new PingCross();

        var first = new PingService();
        cross.BindCrossTarget(first);
        cross.Ping("a");
        Assert.Equal(1, cross.Count());

        // Re-bind to a fresh target (the hot-reload swap): state comes from the new target.
        cross.BindCrossTarget(new PingService());
        Assert.Equal(0, cross.Count());

        cross.UnbindCrossTarget();
        Assert.False(cross.IsCrossBound);
        Assert.Null(cross.Ping("x"));
    }
}
