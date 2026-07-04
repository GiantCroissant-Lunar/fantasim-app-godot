using System;
using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Presentation;

internal readonly record struct BoundarySectionPanelPlan(
    BoundarySectionDocument Section,
    string Name,
    bool DrawSlabGuide);

internal static class BoundarySectionPanelPlanner
{
    private const int MaxPanels = 3;

    public static IReadOnlyList<BoundarySectionPanelPlan> Create(IReadOnlyList<BoundarySectionDocument>? sections)
    {
        if (sections is null || sections.Count == 0)
            return Array.Empty<BoundarySectionPanelPlan>();

        int count = Math.Min(MaxPanels, sections.Count);
        var plans = new BoundarySectionPanelPlan[count];
        for (int i = 0; i < count; i++)
        {
            var section = sections[i];
            plans[i] = new BoundarySectionPanelPlan(
                section,
                Name: $"Section_{section.PlateA}_{section.PlateB}_{section.Kind}",
                DrawSlabGuide: ShouldDrawSlabGuide(section));
        }

        return plans;
    }

    private static bool ShouldDrawSlabGuide(BoundarySectionDocument section)
        => section.Kind == PlateBoundaryKind.Convergent
            && !section.IsCollision
            && section.SubductingPlateId.HasValue;
}
