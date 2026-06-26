using System;
using System.Linq;
using FantaSim.App.Common;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.Cells;
using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Seam;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.World.Seam;

public static class WorldViewComposition
{
    public static void ComposeWorldView(
        HostCompositionContext ctx,
        Godot.SceneTree tree,
        CellElevationModel? cellElevation,
        WorldGenerationRenderOptions renderOptions)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.World");
        // PLAN4-TASK4: onset-aware path — replaces new GlobeReconstructor() (parameterless legacy).
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var roster = OnsetRoster.Build(renderOptions.Seed, onsetTick, renderOptions.TessellationFrequency);
        var schedule = SphereRegimeScheduleDefaults.GeosphereDefault;
        var model = GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, renderOptions.TessellationFrequency);

        // Build the base snapshot at onset (full N-plate globe used for topology caching).
        // GlobePlateSurfaces caches watertight topology from this snapshot — since plate count/cell
        // assignment is fixed, one topology build covers all ticks (including pre-onset lid ticks
        // where caps are hidden by SetRegime(showsPlateFeatures=false)).
        var snapshot = model.BuildGlobeAt(onsetTick);

        // Precompute crust features at evenly-spaced snapshots (one pipeline run); the scrubber snaps
        // to the nearest so dragging stays instant. Features accumulate, so a mountain grows in over ticks.
        // Range: [0, 120 anchors] at every 5 anchors — covers pre-onset (all-zero, gated) + mobile-plate.
        long maxTransportTick = onsetTick + 20_000_000L; // ~20 Ma past onset (well into mobile-plate)
        var snapshotTicks = new System.Collections.Generic.List<long>();
        for (long anchor = 0; anchor <= 120; anchor += 5) snapshotTicks.Add(anchor * snapshot.TicksPerAnchor);
        var featuresByTick = model.RunCrustFeatures(snapshotTicks);
        System.Func<long, byte[]> featuresAt = tick =>
        {
            long best = snapshotTicks[0];
            foreach (var s in snapshotTicks)
                if (System.Math.Abs(s - tick) < System.Math.Abs(best - tick)) best = s;
            return featuresByTick[best];
        };

        // Per-cell elevation feed for the watertight caps: drive sub-project B's CellElevationModel to
        // the tick and hand the seam a per-cell double[] (indexed by cell id). The seam folds it into the
        // T3 GlobePlateSurfaces, which builds one WATERTIGHT per-plate cap per tick via cartography.
        var elevationModel = cellElevation;
        System.Func<long, double[]>? elevationsAt = elevationModel is null ? null : tick =>
        {
            elevationModel.UpdateForTick(tick);
            return elevationModel.GetElevations();
        };

        // T3 (Godot-free) un-shattering: cache the per-plate watertight topology once from the snapshot;
        // the seam rebuilds heights per tick. Replaces the old loose-tile (1 tri/cell, unshared corners)
        // mesh + GPU-compute relief displacement.
        var plateSurfaces = new GlobePlateSurfaces(snapshot);
        var sphereHandoff = TryBuildDefaultSphereHandoff(ctx, log);
        var regimeFieldSampler = new WorldGenerationRegimeFieldSampler();
        Func<long, double?>? magmaSurfaceTemperatureAt = sphereHandoff is null
            ? null
            : tick => regimeFieldSampler.ResolveMagmaOceanSurfaceTemperatureK(tick, sphereHandoff);

        var view = new GlobeView(
            ctx.LoggerFactory,
            ctx.Registry.Get<CrosscutFoundation.Config.IService>(),
            snapshot,
            plateSurfaces,
            tick => CanonicalTimeLabel.ForTick(tick, snapshot.TicksPerAnchor),
            featuresAt,
            elevationsAt,
            magmaSurfaceTemperatureAt);
        // Extend the scrubber to cover the full transport range so the user can drag to onset and beyond.
        view.SetMaxTick(maxTransportTick);
        var generationSubscription = SubscribeWorldGenerationRefresh(ctx, tree, view, log);
        tree.Root.CallDeferred("add_child", view);

        // Build the atmosphere schedule (same onset tick) so the timeline HUD can show both spheres.
        var atmosphereSchedule = SphereRegimeScheduleDefaults.AtmosphereFor(onsetTick);

        // Register the resident ITimelineController adapter. Must happen here (sync, before any
        // deferred EnterAsync calls) so the timeline bundle can resolve it during ActivateAsync.
        var controller = new TimelineController(
            view, schedule, atmosphereSchedule, maxTransportTick);
        var timelineRegistration = ctx.Registry.RegisterOwned<ITimelineController>(
            controller,
            new ServiceRegistration { Tags = new[] { "world", "timeline" }, Description = "World view timeline controller" });

        view.TreeExited += () =>
        {
            generationSubscription.Dispose();
            timelineRegistration.Dispose();
        };

        // Seed the initial regime so GlobeView starts in the correct state before the first tick fires.
        var initialRegime = schedule.RegimeAt(0);
        if (initialRegime is not null)
            view.SetRegime(initialRegime.RegimeId, initialRegime.ShowsPlateFeatures, initialRegime.DefaultColorByField);

        log.LogInformation(
            "World view: globe mounted ({CellCount} cells, {PlateCount} plates, {FeatureSnapshots} feature snapshots, elevationFeed={ElevationFeed}, watertight caps, onset={OnsetTick:N0}, seed={Seed}, freq={TessellationFrequency}); ITimelineController registered",
            snapshot.CellCount,
            snapshot.PlateCount,
            snapshotTicks.Count,
            elevationsAt is not null,
            onsetTick,
            renderOptions.Seed,
            renderOptions.TessellationFrequency);
    }

    private static IDisposable SubscribeWorldGenerationRefresh(HostCompositionContext ctx, Godot.SceneTree tree, GlobeView view, ILogger log)
    {
        var world = ctx.Registry.TryGet<FantaSim.App.World.IService>();
        if (world is null)
            return DelegateDisposable.Empty;

        return world.SubscribeGenerationChanged(evt =>
        {
            if (!WorldGenerationRefreshPolicy.ShouldRefreshGlobe(evt))
                return;

            Callable.From(() =>
            {
                if (!view.IsInsideTree())
                    return;

                var tick = view.Tick;
                view.SetTick(tick);
                log.LogInformation("World view refreshed after generation change at tick={Tick:N0}; detail={Detail}", tick, evt.Detail);
            }).CallDeferred();
        });
    }

    private sealed class DelegateDisposable : IDisposable
    {
        public static readonly IDisposable Empty = new DelegateDisposable(null);

        private readonly Action? _dispose;
        private int _disposed;

        private DelegateDisposable(Action? dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _dispose?.Invoke();
        }
    }

    private static SphereHandoff? TryBuildDefaultSphereHandoff(HostCompositionContext ctx, ILogger log)
    {
        try
        {
            var graphSource = WorldGenerationGraphFamilySource.ForRegime(
                "world-generation",
                WorldGenerationGraphDefaults.BuildFamily(),
                WorldRegimeScheduleKinds.BodyFormation,
                "planetesimal-swarm",
                tick: 0);

            var providers = ctx.Registry
                .GetAll<INodeFunctionProvider>()
                .ToArray();
            var run = new WorldGenerationGraphRunner(providers)
                .RunAsync(graphSource.CompileForExecution().Document)
                .GetAwaiter()
                .GetResult();

            if (run.SphereHandoff is not null)
            {
                log.LogInformation(
                    "World graph handoff: retainedHeatJ={RetainedHeatJ:E3}, products={ProductsCount}",
                    run.SphereHandoff.RetainedHeatJ,
                    run.Products.Count);
            }

            return run.SphereHandoff;
        }
        catch (Exception ex)
        {
            log.LogWarning("World graph handoff unavailable: {Message}", ex.Message);
            return null;
        }
    }
}
