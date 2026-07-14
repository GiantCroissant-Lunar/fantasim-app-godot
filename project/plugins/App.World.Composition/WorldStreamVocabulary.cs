using System.Net;
using FantaSim.World.TruthStream;

namespace FantaSim.App.World.Composition;

/// <summary>
/// The ONLY production minting point for world-scoped five-axis stream identities. Every factory
/// hard-codes the doctrine-correct <c>LLevel</c> per
/// <c>fantasim-world/vault/architecture/planet-stack-model.md</c> S2: L3 stellar, <b>L2 world</b>,
/// L1 regional, L0 local, negative micro. Plate topology and every world-scoped app stream are L2.
/// </summary>
/// <remarks>
/// Centralising identity construction here makes the L-doctrine a compile-time gate rather than a
/// convention spread across call sites. The architecture guard test
/// (<c>StreamVocabularyGuardTests</c>) source-scans production files to prove no inline
/// <c>new TruthStreamIdentity(</c> or <c>new LayerTrackStreamId(</c> survives outside this class
/// and the codec decode paths.
/// </remarks>
public static class WorldStreamVocabulary
{
    /// <summary>
    /// L-axis doctrine: world-scoped causal write-authority. See planet-stack-model.md S2.
    /// </summary>
    public const int WorldLLevel = 2;

    /// <summary>
    /// The plate-topology truth stream for a given world/branch:
    /// <c>{world}:{branch}:L2:geosphere:plates</c>.
    /// </summary>
    public static TruthStreamIdentity Plates(string worldId, string branchId)
    {
        ValidateWorldId(worldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        return new TruthStreamIdentity(worldId, branchId, WorldLLevel, "geosphere", "plates");
    }

    /// <summary>
    /// The engine-emitted plate-topology truth stream consumed at onset:
    /// <c>default:main:L2:geo.plates.topology:M0</c>. These five values MUST match the engine's
    /// plate-topology stream config (fantasim-world PlateTopologyEmitter.EmitRoster call path) —
    /// if the engine config changes, update this factory in lockstep. Note this is the
    /// doctrine-correct dotted-domain + M0 form (lrm-axis-model: Domain vs M); the bare-domain
    /// factories above predate that rule and their migration is an owed decision.
    /// </summary>
    public static TruthStreamIdentity PlateTopologyTruth()
        => new("default", "main", WorldLLevel, "geo.plates.topology", "M0");

    /// <summary>
    /// The rotation-import control stream for a given world/branch:
    /// <c>{world}:{branch}:L2:world:imports</c>. Used by the prepare/bind CAS protocol.
    /// </summary>
    public static TruthStreamIdentity ImportsControl(string worldId, string branchId)
    {
        ValidateWorldId(worldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        return new TruthStreamIdentity(worldId, branchId, WorldLLevel, "world", "imports");
    }

    /// <summary>
    /// The app-owned active rotation-source selection stream:
    /// <c>app:main:L2:world:rotation-bindings</c>. Records the terminal imported/generated marker.
    /// </summary>
    public static TruthStreamIdentity RotationSelection()
        => new("app", "main", WorldLLevel, "world", "rotation-bindings");

    /// <summary>
    /// The app-owned world generation truth stream:
    /// <c>app:main:L2:world:default</c>.
    /// </summary>
    public static TruthStreamIdentity Generation()
        => new("app", "main", WorldLLevel, "world", "default");

    /// <summary>
    /// The default <see cref="LayerTrackStreamId"/> for layer-track descriptors produced by the
    /// pipeline node handlers. Doctrine-correct axis ordering: variation=<c>"default"</c>,
    /// branch=<c>"main"</c>, L=<c>"L2"</c>.
    /// </summary>
    public static LayerTrackStreamId TrackDefault()
        => new("default", "main", "L2", "world", "default");

    /// <summary>
    /// The <see cref="LayerTrackStreamId"/> for the discovered truth-events track in
    /// <c>WorldPlugin</c>'s stream-discovery seam. Mirrors the <see cref="Generation"/> truth
    /// stream identity as a track-layer view: variation=<c>"app"</c>, branch=<c>"main"</c>,
    /// L=<c>"L2"</c>.
    /// </summary>
    public static LayerTrackStreamId TruthEventsTrack()
        => new("app", "main", "L2", "world", "default");

    /// <summary>
    /// Validates that a worldId is not empty/whitespace and does not parse as an IP address.
    /// The HTTP ingress path can leak caller IPs into WorldId; this guard makes that loud at mint
    /// time until the ingress mapping is redesigned.
    /// </summary>
    private static void ValidateWorldId(string worldId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        if (IPAddress.TryParse(worldId, out _))
        {
            throw new ArgumentException(
                $"WorldId '{worldId}' parses as an IP address; caller IPs must not leak into stream identities.",
                nameof(worldId));
        }
    }
}
