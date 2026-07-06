using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrosscutFoundation.Messaging;
using FantaSim.App.Camera;
using FantaSim.App.Camera.Providers;
using FantaSim.App.Camera.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Camera.Tests;

/// <summary>
/// Unit tests for the engine-agnostic T3 <see cref="Service"/>: spec validation, active-id
/// tracking, message routing, and bus subscription. Mirrors the shape of App.Timeline.Tests /
/// App.Render.Tests: pure C# (no Godot); a fake <see cref="ICameraRig"/> and fake
/// <see cref="IMessageBus"/> replace the T4 seam and the crosscut bus.
/// </summary>
public class CameraServiceTests
{
    private sealed class FakeRig : ICameraRig
    {
        public List<CameraSpec> Registered { get; } = new();
        public List<string> Activated { get; } = new();
        public List<string> Unregistered { get; } = new();

        public Task RegisterAsync(CameraSpec spec)
        {
            Registered.Add(spec);
            return Task.CompletedTask;
        }

        public Task ActivateAsync(string cameraId)
        {
            Activated.Add(cameraId);
            return Task.CompletedTask;
        }

        public Task UnregisterAsync(string cameraId)
        {
            Unregistered.Add(cameraId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMessageBus : IMessageBus
    {
        public List<object> Subscriptions { get; } = new();
        public List<object> Published { get; } = new();

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            Subscriptions.Add(typeof(T));
            return new SubscriptionToken(() => { });
        }

        public void Publish<T>(T message) => Published.Add(message!);

        private sealed class SubscriptionToken : IDisposable
        {
            private Action? _onDispose;
            public SubscriptionToken(Action? onDispose) => _onDispose = onDispose;
            public void Dispose()
            {
                _onDispose?.Invoke();
                _onDispose = null;
            }
        }
    }

    private static (Service svc, FakeRig rig, FakeMessageBus bus) Build()
    {
        var rig = new FakeRig();
        var bus = new FakeMessageBus();
        var svc = new Service(rig, bus, NullLoggerFactory.Instance);
        return (svc, rig, bus);
    }

    private static CameraSpec Spec(string id, string viewport = "main") =>
        new(id, new System.Numerics.Vector3(0, 0, 10), System.Numerics.Vector3.Zero, viewport);

    [Fact]
    public void Constructor_SubscribesToActivateCameraMessage()
    {
        var (_, _, bus) = Build();
        Assert.Contains(typeof(ActivateCameraMessage), bus.Subscriptions);
    }

    [Fact]
    public async Task RegisterAsync_AddsSpec_AndForwardsToRig()
    {
        var (svc, rig, _) = Build();
        var spec = Spec("cam-a");
        await svc.RegisterAsync(spec);
        Assert.Single(rig.Registered);
        Assert.Equal("cam-a", rig.Registered[0].CameraId);
        Assert.Single(svc.RegisteredCameras);
        Assert.Equal("cam-a", svc.RegisteredCameras[0].CameraId);
    }

    [Fact]
    public async Task RegisterAsync_ReplacesExistingSpec_ForSameId()
    {
        var (svc, rig, _) = Build();
        await svc.RegisterAsync(Spec("cam-a"));
        var updated = new CameraSpec("cam-a", new System.Numerics.Vector3(0, 0, 20), System.Numerics.Vector3.Zero);
        await svc.RegisterAsync(updated);
        Assert.Equal(2, rig.Registered.Count);
        Assert.Single(svc.RegisteredCameras);
        Assert.Equal(20f, svc.RegisteredCameras[0].Position.Z);
    }

    [Fact]
    public async Task RegisterAsync_RejectsNullOrWhitespaceId()
    {
        var (svc, _, _) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegisterAsync(Spec("")));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegisterAsync(Spec("   ")));
    }

    [Fact]
    public async Task ActivateAsync_UnknownId_Throws()
    {
        var (svc, _, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ActivateAsync("nope"));
    }

    [Fact]
    public async Task ActivateAsync_ForwardsToRig_AndTracksActive()
    {
        var (svc, rig, _) = Build();
        await svc.RegisterAsync(Spec("cam-a"));
        await svc.RegisterAsync(Spec("cam-b"));
        await svc.ActivateAsync("cam-a");
        Assert.Equal("cam-a", svc.ActiveCameraId("main"));
        Assert.Contains("cam-a", rig.Activated);
    }

    [Fact]
    public async Task ActivateAsync_SameCamera_Twice_IsNoOp_OnSecondCall()
    {
        var (svc, rig, _) = Build();
        await svc.RegisterAsync(Spec("cam-a"));
        await svc.ActivateAsync("cam-a");
        await svc.ActivateAsync("cam-a");
        Assert.Single(rig.Activated);
    }

    [Fact]
    public async Task ActivateAsync_TracksActivePerViewport()
    {
        var (svc, rig, _) = Build();
        await svc.RegisterAsync(Spec("cam-main", "main"));
        await svc.RegisterAsync(Spec("cam-mini", "mini"));
        await svc.ActivateAsync("cam-main");
        await svc.ActivateAsync("cam-mini");
        Assert.Equal("cam-main", svc.ActiveCameraId("main"));
        Assert.Equal("cam-mini", svc.ActiveCameraId("mini"));
    }

    [Fact]
    public async Task UnregisterAsync_RemovesSpec_AndForwardsToRig()
    {
        var (svc, rig, _) = Build();
        await svc.RegisterAsync(Spec("cam-a"));
        await svc.UnregisterAsync("cam-a");
        Assert.Empty(svc.RegisteredCameras);
        Assert.Contains("cam-a", rig.Unregistered);
    }

    [Fact]
    public async Task UnregisterAsync_UnknownId_IsNoOp()
    {
        var (svc, rig, _) = Build();
        await svc.UnregisterAsync("never-registered");
        Assert.Empty(rig.Unregistered);
        Assert.Empty(svc.RegisteredCameras);
    }

    [Fact]
    public async Task UnregisterAsync_ActiveCamera_ClearsActiveForViewport()
    {
        var (svc, rig, _) = Build();
        await svc.RegisterAsync(Spec("cam-a"));
        await svc.ActivateAsync("cam-a");
        await svc.UnregisterAsync("cam-a");
        Assert.Null(svc.ActiveCameraId("main"));
        Assert.Contains("cam-a", rig.Unregistered);
    }

    [Fact]
    public async Task CamerasChanged_RaisedOnRegister_Activate_AndUnregister()
    {
        var (svc, _, _) = Build();
        var raises = 0;
        svc.CamerasChanged += () => raises++;
        await svc.RegisterAsync(Spec("cam-a"));
        await svc.ActivateAsync("cam-a");
        await svc.UnregisterAsync("cam-a");
        Assert.Equal(3, raises);
    }
}

/// <summary>
/// Pure-engine checks that the T3 App.Camera plugin assembly stays Godot-free (C1 lock), mirroring
/// App.Render.Tests/T3PurityTests. The seam (App.Camera.Seam) holds all Godot types; the T3
/// orchestrator and the T1 contract must never reference GodotSharp.
/// </summary>
public class T3PurityTests
{
    [Fact]
    public void T3_Assembly_HasNoGodotReference()
    {
        var asm = typeof(FantaSim.App.Camera.Services.Service).Assembly;
        var referenced = asm.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, r => r.Name == "GodotSharp");
    }

    [Fact]
    public void Contract_Assembly_HasNoGodotReference()
    {
        var asm = typeof(CameraSpec).Assembly;
        var referenced = asm.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, r => r.Name == "GodotSharp");
    }
}