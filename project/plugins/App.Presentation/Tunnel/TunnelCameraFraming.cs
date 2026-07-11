using System.Numerics;

namespace FantaSim.App.Presentation.Tunnel;

internal static class TunnelCameraFraming
{
    internal const float FieldOfViewDegrees = 55.0f;
    internal static readonly Vector3 LocalPosition = new(18.0f, 10.0f, 44.0f);
    internal static readonly Vector3 LocalTarget = new(0.0f, -7.0f, -8.0f);
}
