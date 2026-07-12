namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelSnapshotSourceState(
    bool SphereVisible,
    bool UnavailableVisible);

/// <summary>Exact source-provenance allowlist for tunnel snapshot spheres. Procedural or unknown
/// maps remain explicit unavailable content even though the ordinary 2D timeline may render them.</summary>
internal static class TunnelSnapshotSourcePolicy
{
    internal static bool IsReal(string? sourceKind)
        => sourceKind is "crust-low-res" or "plate-low-res" or "mantle-shell-low-res";

    internal static TunnelSnapshotSourceState StateFor(string? sourceKind)
    {
        var real = IsReal(sourceKind);
        return new TunnelSnapshotSourceState(real, !real);
    }
}
