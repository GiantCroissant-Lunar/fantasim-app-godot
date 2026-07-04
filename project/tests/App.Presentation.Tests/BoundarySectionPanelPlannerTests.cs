using System.Collections.Generic;
using FantaSim.App.Presentation;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using Xunit;

namespace App.Presentation.Tests;

public sealed class BoundarySectionPanelPlannerTests
{
    [Fact]
    public void Create_limits_panels_to_three_and_marks_only_subduction_sections_with_slab_guides()
    {
        var plans = BoundarySectionPanelPlanner.Create(new[]
        {
            Section(0, 1, PlateBoundaryKind.Convergent, subductingPlateId: 1),
            Section(2, 3, PlateBoundaryKind.Divergent),
            Section(4, 5, PlateBoundaryKind.Transform),
            Section(6, 7, PlateBoundaryKind.Convergent, subductingPlateId: 7),
        });

        Assert.Equal(3, plans.Count);
        Assert.Equal("Section_0_1_Convergent", plans[0].Name);
        Assert.True(plans[0].DrawSlabGuide);
        Assert.False(plans[1].DrawSlabGuide);
        Assert.False(plans[2].DrawSlabGuide);
    }

    [Fact]
    public void Create_does_not_mark_collision_convergent_sections_with_slab_guides()
    {
        var plan = Assert.Single(BoundarySectionPanelPlanner.Create(new[]
        {
            Section(0, 1, PlateBoundaryKind.Convergent, subductingPlateId: null, isCollision: true),
        }));

        Assert.False(plan.DrawSlabGuide);
    }

    private static BoundarySectionDocument Section(
        int plateA,
        int plateB,
        PlateBoundaryKind kind,
        int? subductingPlateId = null,
        bool isCollision = false)
        => new(
            PlateA: plateA,
            PlateB: plateB,
            Kind: kind,
            Origin: new GlobeVec3(1, 0, 0),
            NormalAxis: new GlobeVec3(0, 1, 0),
            Samples: Samples(kind),
            InteriorBands: Bands(),
            Exaggeration: 1.0,
            PlanetRadiusMetres: 6_371_000.0,
            LabelOverride: null,
            SubductingPlateId: subductingPlateId,
            IsCollision: isCollision);

    private static IReadOnlyList<BoundarySectionSample> Samples(PlateBoundaryKind kind)
        => new[]
        {
            new BoundarySectionSample(-0.02, -1_000.0, 35_000.0, kind),
            new BoundarySectionSample(0.0, 0.0, 35_000.0, kind),
            new BoundarySectionSample(0.02, 1_200.0, 35_000.0, kind),
        };

    private static IReadOnlyList<BoundarySectionBand> Bands()
        => new[]
        {
            new BoundarySectionBand("mantle", new BoundarySectionColor(0.45, 0.18, 0.08), 0.98, 0.93),
            new BoundarySectionBand("crust", new BoundarySectionColor(0.55, 0.50, 0.42), 1.0, 0.98),
        };
}
