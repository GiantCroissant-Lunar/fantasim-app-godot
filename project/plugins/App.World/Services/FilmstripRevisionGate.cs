using System;

namespace FantaSim.App.World;

/// <summary>Rejects a filmstrip render unless its requested revision remained current for the
/// complete render. This prevents caller-stamped provenance when generation changes mid-frame.</summary>
internal static class FilmstripRevisionGate
{
    internal static bool IsStable(int requested, int start, int completed)
        => requested == start && start == completed;

    internal static T? RenderIfStable<T>(
        int requested,
        Func<int> readRevision,
        Func<int, T> render)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(readRevision);
        ArgumentNullException.ThrowIfNull(render);

        var start = readRevision();
        if (requested != start)
            return null;

        var result = render(start);
        var completed = readRevision();
        return IsStable(requested, start, completed) ? result : null;
    }
}
