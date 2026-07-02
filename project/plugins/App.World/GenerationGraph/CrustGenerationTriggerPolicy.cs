using System;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Immutable cache key for one auto-triggered crust-generation run.
/// Windowed by tick so repeated scrubbing within the same window does not re-run the pipeline.
/// Graph revision is included so graph edits invalidate the cache.
/// </summary>
public sealed record CrustGenerationTriggerKey(int GraphRevision, long WindowIndex);

/// <summary>
/// Decision produced by <see cref="CrustGenerationTriggerPolicy.Evaluate"/> for a playhead position.
/// </summary>
public sealed record CrustGenerationTriggerDecision(
    bool ShouldRun,
    bool ShouldCancel,
    long CanonicalTick,
    CrustGenerationTriggerKey? Key);

/// <summary>
/// Pure decision logic for automatic crust-generation triggers. Given the active regime id,
/// graph revision, and playhead tick, it decides whether to run the pipeline, whether to cancel
/// any in-flight run, and the canonical tick / cache key to use.
/// </summary>
public sealed class CrustGenerationTriggerPolicy
{
    private const string MobilePlateRegimeId = "mobile-plate";

    private readonly long _windowSize;

    public CrustGenerationTriggerPolicy(long windowSize)
    {
        if (windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be positive.");

        _windowSize = windowSize;
    }

    /// <summary>
    /// Evaluate the current playhead position.
    /// </summary>
    /// <param name="regimeId">Active regime id from the geosphere schedule, or null when no regime applies.</param>
    /// <param name="graphRevision">Current generation-graph family revision.</param>
    /// <param name="tick">Current playhead tick.</param>
    /// <returns>
    /// When the playhead is in the mobile-plate regime, returns <c>ShouldRun=true</c> with a
    /// quantized <see cref="CanonicalTick"/> and <see cref="Key"/> identifying the (revision, window).
    /// For any other regime (or null), returns <c>ShouldCancel=true</c> so in-flight work can be dropped.
    /// </returns>
    public CrustGenerationTriggerDecision Evaluate(string? regimeId, int graphRevision, long tick)
    {
        if (!string.Equals(regimeId, MobilePlateRegimeId, StringComparison.Ordinal))
        {
            return new CrustGenerationTriggerDecision(
                ShouldRun: false,
                ShouldCancel: true,
                CanonicalTick: tick,
                Key: null);
        }

        long windowIndex = tick / _windowSize;
        long canonicalTick = windowIndex * _windowSize;

        return new CrustGenerationTriggerDecision(
            ShouldRun: true,
            ShouldCancel: false,
            CanonicalTick: canonicalTick,
            Key: new CrustGenerationTriggerKey(graphRevision, windowIndex));
    }
}
