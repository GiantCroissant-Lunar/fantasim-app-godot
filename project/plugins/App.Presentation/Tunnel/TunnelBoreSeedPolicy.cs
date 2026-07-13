using System;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>
/// Stable seed derivation from the stream-identity branch axis. FNV-1a 64-bit over UTF-16 code
/// units (low byte, then high byte, per char) — deterministic across processes and platforms,
/// unlike string.GetHashCode.
/// </summary>
internal static class TunnelBoreSeedPolicy
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    internal static long SeedFor(string? branchId)
    {
        var branch = string.IsNullOrWhiteSpace(branchId) ? "main" : branchId.Trim();
        var hash = OffsetBasis;
        foreach (var ch in branch)
        {
            hash = (hash ^ (byte)(ch & 0xFF)) * Prime;
            hash = (hash ^ (byte)(ch >> 8)) * Prime;
        }

        return unchecked((long)hash);
    }
}
