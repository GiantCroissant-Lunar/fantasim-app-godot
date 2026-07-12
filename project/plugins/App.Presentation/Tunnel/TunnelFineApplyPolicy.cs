using System;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelFineApplyReadiness(
    bool BinderAlive,
    int ExpectedGeneration,
    int CurrentGeneration,
    long ExpectedEpoch,
    long CurrentEpoch,
    long ExpectedBucket,
    long? CurrentBucket,
    int ExpectedGraphRevision,
    int CurrentGraphRevision,
    string ExpectedSphereId,
    string? CurrentSphereId,
    string ExpectedLayerId,
    string? CurrentLayerId);

internal static class TunnelFineApplyPolicy
{
    internal static bool CanApply(TunnelFineApplyReadiness readiness)
        => readiness.BinderAlive
            && readiness.ExpectedGeneration == readiness.CurrentGeneration
            && readiness.ExpectedEpoch == readiness.CurrentEpoch
            && readiness.CurrentBucket == readiness.ExpectedBucket
            && readiness.ExpectedGraphRevision == readiness.CurrentGraphRevision
            && string.Equals(
                readiness.ExpectedSphereId,
                readiness.CurrentSphereId,
                StringComparison.Ordinal)
            && string.Equals(
                readiness.ExpectedLayerId,
                readiness.CurrentLayerId,
                StringComparison.Ordinal);
}
