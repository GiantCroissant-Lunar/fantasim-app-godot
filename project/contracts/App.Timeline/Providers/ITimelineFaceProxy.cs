namespace FantaSim.App.Timeline.Providers;

/// <summary>
/// Shared surface for the generated cross-ALC timeline face proxy. The concrete proxy lives in
/// the collectible timeline bundle; the resident face binds itself through this contract.
/// </summary>
public interface ITimelineFaceProxy : ITimelineFace
{
    bool IsCrossBound { get; }

    void BindCrossTarget(ITimelineFace target);

    void UnbindCrossTarget();
}
