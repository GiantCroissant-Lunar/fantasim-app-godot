using FantaSim.App.World.Composition;

namespace App.Timeline.Tests;

internal static class TimelineTestSchedules
{
    public const long PlateOnsetTick = 100_000_000;

    public static SphereRegimeSchedule Geosphere()
        => new(
            new SphereId("geosphere"),
            new[]
            {
                new SphereRegime(
                    "magma-ocean",
                    0,
                    1_000_000,
                    new[] { new LayerId("geosphere.magma-ocean") },
                    ShowsPlateFeatures: false),
                new SphereRegime(
                    "stagnant-lid",
                    1_000_000,
                    PlateOnsetTick,
                    new[] { new LayerId("geosphere.stagnant-lid") },
                    ShowsPlateFeatures: false),
                new SphereRegime(
                    "mobile-plate",
                    PlateOnsetTick,
                    SphereRegime.OpenEnd,
                    new[] { new LayerId("geosphere.plate"), new LayerId("geosphere.crust") }),
            });

    public static SphereRegimeSchedule Atmosphere()
        => new(
            new SphereId("atmosphere"),
            new[]
            {
                new SphereRegime(
                    "primordial-steam",
                    0,
                    PlateOnsetTick,
                    new[] { new LayerId("atmosphere.steam") },
                    ShowsPlateFeatures: false),
                new SphereRegime(
                    "coupled-climate",
                    PlateOnsetTick,
                    SphereRegime.OpenEnd,
                    new[] { new LayerId("atmosphere.climate") }),
            });
}
