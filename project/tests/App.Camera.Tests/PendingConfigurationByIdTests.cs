using System;
using System.Collections.Generic;
using FantaSim.App.Camera;
using Xunit;

namespace App.Camera.Tests;

public sealed class PendingConfigurationByIdTests
{
    [Fact]
    public void ApplyOrPend_RemembersRequest_WhenKeyIsNotReady()
    {
        var sut = new PendingConfigurationById<string>();
        var applied = new List<string>();

        var result = sut.ApplyOrPend(
            "globe.default",
            "follow-target",
            _ => false,
            (_, request) => applied.Add(request));

        Assert.False(result);
        Assert.True(sut.HasPending("globe.default"));
        Assert.Equal(1, sut.Count);
        Assert.Empty(applied);
    }

    [Fact]
    public void TryApplyPending_AppliesOnce_WhenKeyBecomesReady()
    {
        var sut = new PendingConfigurationById<string>();
        var applied = new List<string>();

        sut.ApplyOrPend("globe.default", "target-a", _ => false, (_, _) => { });

        Assert.True(sut.TryApplyPending(
            "globe.default",
            _ => true,
            (id, request) => applied.Add($"{id}:{request}")));

        Assert.False(sut.HasPending("globe.default"));
        Assert.Equal(0, sut.Count);
        Assert.Single(applied);
        Assert.Equal("globe.default:target-a", applied[0]);

        Assert.False(sut.TryApplyPending(
            "globe.default",
            _ => true,
            (_, request) => applied.Add(request)));
        Assert.Single(applied);
    }

    [Fact]
    public void ApplyOrPend_AppliesImmediately_WhenKeyIsReady()
    {
        var sut = new PendingConfigurationById<int>();
        var applied = new List<int>();

        var result = sut.ApplyOrPend(
            "globe.default",
            4,
            _ => true,
            (_, request) => applied.Add(request));

        Assert.True(result);
        Assert.False(sut.HasPending("globe.default"));
        Assert.Equal(0, sut.Count);
        Assert.Single(applied);
        Assert.Equal(4, applied[0]);
    }

    [Fact]
    public void LaterPendingRequest_ReplacesEarlierRequest_ForSameKey()
    {
        var sut = new PendingConfigurationById<string>();
        var applied = new List<string>();

        sut.ApplyOrPend("globe.default", "target-a", _ => false, (_, _) => { });
        sut.ApplyOrPend("globe.default", "target-b", _ => false, (_, _) => { });

        Assert.Equal(1, sut.Count);
        Assert.True(sut.TryApplyPending(
            "globe.default",
            _ => true,
            (_, request) => applied.Add(request)));

        Assert.Single(applied);
        Assert.Equal("target-b", applied[0]);
    }

    [Fact]
    public void ApplyOrPend_ClearsStalePendingRequest_WhenKeyIsReady()
    {
        var sut = new PendingConfigurationById<string>();
        var applied = new List<string>();

        sut.ApplyOrPend("globe.default", "target-a", _ => false, (_, _) => { });

        Assert.True(sut.ApplyOrPend(
            "globe.default",
            "target-b",
            _ => true,
            (_, request) => applied.Add(request)));

        Assert.False(sut.HasPending("globe.default"));
        Assert.Equal(0, sut.Count);
        Assert.Single(applied);
        Assert.Equal("target-b", applied[0]);
    }

    [Fact]
    public void TryApplyPending_KeepsRequest_WhileKeyIsStillNotReady()
    {
        var sut = new PendingConfigurationById<string>();
        var applied = new List<string>();

        sut.ApplyOrPend("globe.default", "target-a", _ => false, (_, _) => { });

        Assert.False(sut.TryApplyPending(
            "globe.default",
            _ => false,
            (_, request) => applied.Add(request)));

        Assert.True(sut.HasPending("globe.default"));
        Assert.Equal(1, sut.Count);
        Assert.Empty(applied);
    }

    [Fact]
    public void Rejects_Null_Callbacks()
    {
        var sut = new PendingConfigurationById<string>();

        Assert.Throws<ArgumentNullException>(() =>
            sut.ApplyOrPend("globe.default", "target", null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() =>
            sut.ApplyOrPend("globe.default", "target", _ => true, null!));
        Assert.Throws<ArgumentNullException>(() =>
            sut.TryApplyPending("globe.default", null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() =>
            sut.TryApplyPending("globe.default", _ => true, null!));
    }
}
