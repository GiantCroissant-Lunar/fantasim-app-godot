using System;
using System.Collections.Generic;
using FantaSim.App.Camera;
using Xunit;

namespace App.Camera.Tests;

/// <summary>
/// Unit tests for the retry/bind-once state machine extracted from GlobeOrbitControls. The
/// PhantomCameraHost is a Godot Node (untestable here), but the poll/bind shape is Godot-free;
/// these tests pin that contract so the lazy camera-orbit bind behaves deterministically.
/// </summary>
public sealed class LazyBindOnceTests
{
    [Fact]
    public void Stays_Unbound_While_Provider_Returns_Null()
    {
        var bound = new List<string>();
        var sut = new LazyBindOnce<string>(bound.Add);

        Assert.False(sut.IsBound);

        Assert.False(sut.TryResolve(() => null));
        Assert.False(sut.IsBound);
        Assert.Null(sut.Value);
        Assert.Empty(bound);
    }

    [Fact]
    public void Binds_On_First_Non_Null_Then_Becomes_Inert()
    {
        var bound = new List<string>();
        var sut = new LazyBindOnce<string>(bound.Add);
        var providerCalls = 0;

        string? Provider()
        {
            providerCalls++;
            return "host";
        }

        Assert.True(sut.TryResolve(Provider));
        Assert.True(sut.IsBound);
        Assert.Equal("host", sut.Value);
        Assert.Single(bound);
        Assert.Equal(1, providerCalls);

        // Subsequent resolves must not re-invoke the provider or the callback, and the value is sticky.
        Assert.True(sut.TryResolve(Provider));
        Assert.True(sut.IsBound);
        Assert.Equal("host", sut.Value);
        Assert.Single(bound);
        Assert.Equal(1, providerCalls);
    }

    [Fact]
    public void Binds_On_A_Later_Poll_After_Nulls()
    {
        var bound = new List<string>();
        var sut = new LazyBindOnce<string>(bound.Add);
        var available = false;

        Assert.False(sut.TryResolve(() => available ? "host" : null));
        Assert.False(sut.IsBound);

        available = true;
        Assert.True(sut.TryResolve(() => available ? "host" : null));
        Assert.True(sut.IsBound);
        Assert.Single(bound);
    }

    [Fact]
    public void Constructor_Rejects_Null_Callback()
        => Assert.Throws<ArgumentNullException>(() => new LazyBindOnce<string>(null!));
}
