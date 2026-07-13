using System;
using System.Collections.Generic;
using MessagePack;

namespace FantaSim.App.World.Persistence;

/// <summary>
/// Cross-session persisted crust-product cache entry (2026-07-11 persistence slice 1,
/// vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md §4.1). Carries the same six
/// world-identity key fields as the in-memory <c>Service.CrustProductCacheKey</c> (Seed, Frequency,
/// SpinRateRadiansPerMegaAnnum, GraphRevision, RotationAuthorityDigest, SnapshotTick) plus the two
/// invalidation stamps (§5): <see cref="SchemaVersion"/> (bumped by hand on DTO/tick-scale-constant
/// changes) and <see cref="AppVersionStamp"/> (the writing build's Module Version ID — see
/// <see cref="CrustProductCacheSchema.CurrentAppVersionStamp"/> for why MVID and not an assembly
/// version number).
///
/// Payload is deliberately NOT the full CrustEvolutionResult/WorldCrustMaterialization object graph:
/// that graph's Tessellation/Topology/Plates fields are cheap, PURE functions of
/// (Seed, Frequency, SpinRateRadiansPerMegaAnnum) — already recomputed on every cache miss before the
/// expensive step runs (WorldCrustMaterializer.MaterializeAsync's first two lines) — and its
/// Store/Stream/Reduced fields are truth-stream plumbing no App.World consumer reads after the fact
/// (verified by grep across Service.cs for ".Result.Store"/".Result.Stream"/".Result.Reduced" — zero
/// hits). The only genuinely expensive, non-reconstructible output is the crust-evolution fold:
/// <see cref="CellStates"/>/<see cref="Features"/> mirror CrustEvolutionResult.StateByTick/
/// FeaturesByTick AT THE SINGLE SnapshotTick this record's key names (WorldCrustRunSpec.
/// ForPresentation always requests exactly one snapshot tick), flattened to lists because the
/// engine's CellCrustState/CrustFeature types are fantasim-world types this contracts project must
/// not reference (T1 stays engine-free; App.World does the mapping — see
/// Crust/WorldCrustMaterializer.cs FromPersistedState/ToPersistedRecord).
/// </summary>
[MessagePackObject]
public sealed record CrustProductCacheRecord(
    [property: Key(0)] int Seed,
    [property: Key(1)] int Frequency,
    [property: Key(2)] double SpinRateRadiansPerMegaAnnum,
    [property: Key(3)] int GraphRevision,
    [property: Key(4)] long SnapshotTick,
    [property: Key(5)] int SchemaVersion,
    [property: Key(6)] string AppVersionStamp,
    [property: Key(7)] IReadOnlyList<CellCrustStateRecord> CellStates,
    [property: Key(8)] IReadOnlyList<CrustFeatureRecord> Features,
    [property: Key(9)] string RotationAuthorityDigest);

/// <summary>
/// One cell's accumulated crust state at the snapshot tick. Mirrors fantasim-world's
/// <c>CellCrustState</c> (Geosphere.Crust/CrustState.cs) field-for-field.
/// </summary>
[MessagePackObject]
public sealed record CellCrustStateRecord(
    [property: Key(0)] int CellId,
    [property: Key(1)] double ContinentalFraction,
    [property: Key(2)] double OrogenicPressure,
    [property: Key(3)] double VolcanicActivity,
    [property: Key(4)] double CrustAgeTicks);

/// <summary>
/// One cell's derived feature at the snapshot tick. Mirrors fantasim-world's <c>CrustFeature</c>
/// (Geosphere.Crust/CrustFeatures.cs); <see cref="Kind"/> is the raw <c>CrustFeatureKind</c> byte
/// value (None=0, Mountain, VolcanicArc, Trench, Ridge, Fault).
/// </summary>
[MessagePackObject]
public sealed record CrustFeatureRecord(
    [property: Key(0)] int CellId,
    [property: Key(1)] byte Kind,
    [property: Key(2)] double Magnitude);

/// <summary>
/// Schema version, document-id composition, and MessagePack encode/decode for
/// <see cref="CrustProductCacheRecord"/>. Kept next to the record so id composition and payload
/// shape change together.
/// </summary>
public static class CrustProductCacheSchema
{
    /// <summary>Collection name in the resident document store (spec §4.1).</summary>
    public const string Collection = "crustProducts";

    /// <summary>
    /// Bump on ANY change to this record's shape OR to a tick-scale/canonical-tick-spacing constant
    /// the crust pipeline depends on (spec §5 point 4 — the G35 MaxTick-rescale precedent: a
    /// persisted entry keyed by a pre-rescale SnapshotTick must not silently deserialize into a
    /// tick value the current build can no longer reach by scrubbing). A bump changes every future
    /// document id, so old rows simply stop being requested — fail-soft miss, never a
    /// deserialization crash on boot.
    /// </summary>
    public const int SchemaVersion = 2;

    /// <summary>
    /// The running build's Module Version ID (a fresh GUID the compiler stamps on every
    /// compilation, even for byte-identical source) — spec §5 point 5 / task decision point 6's
    /// "app-version stamp". Chosen over an assembly version number because this repo has no
    /// GitVersion wiring (checked: no GitVersion/InformationalVersion property in
    /// project/Directory.Build.props), so a hand-set assembly version would sit static forever and
    /// never actually invalidate anything. The MVID has exactly the property this stamp needs:
    /// STABLE across restarts of the same exported binary (the windowed warm-boot gate is
    /// cold-boot → seek → restart same binary → same seek must be warm), but DIFFERENT across any
    /// rebuild — including one where a developer changes a tick-scale constant but forgets to bump
    /// <see cref="SchemaVersion"/> by hand, exactly the failure mode decision point 6 asks this
    /// second stamp to catch. Over-invalidation (an unrelated rebuild also busts the cache) is the
    /// safe failure direction for a cache; under-invalidation is not.
    /// </summary>
    public static string CurrentAppVersionStamp { get; } =
        typeof(CrustProductCacheSchema).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>
    /// Deterministic document id: every key field the in-memory <c>Service.CrustProductCacheKey</c>
    /// carries (Seed, Frequency, SpinRateRadiansPerMegaAnnum, GraphRevision,
    /// RotationAuthorityDigest, SnapshotTick) plus both invalidation stamps, composed once so a
    /// schema-version bump, authority switch, or app rebuild produces a different id rather than
    /// requiring a separate post-fetch version check (spec §4.1/§5).
    /// </summary>
    public static string ComposeDocumentId(
        int seed,
        int frequency,
        double spinRateRadiansPerMegaAnnum,
        int graphRevision,
        string rotationAuthorityDigest,
        long snapshotTick,
        int schemaVersion,
        string appVersionStamp)
    {
        ArgumentNullException.ThrowIfNull(appVersionStamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(rotationAuthorityDigest);
        return FormattableString.Invariant(
            $"{seed}:{frequency}:{spinRateRadiansPerMegaAnnum:R}:{graphRevision}:{rotationAuthorityDigest}:{snapshotTick}:{schemaVersion}:{appVersionStamp}");
    }

    public static byte[] Encode(CrustProductCacheRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return MessagePackSerializer.Serialize(record);
    }

    public static CrustProductCacheRecord Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return MessagePackSerializer.Deserialize<CrustProductCacheRecord>(bytes);
    }
}
