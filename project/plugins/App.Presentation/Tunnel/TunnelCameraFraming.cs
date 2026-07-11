using System.Numerics;

namespace FantaSim.App.Presentation.Tunnel;

internal static class TunnelCameraFraming
{
    internal const float FieldOfViewDegrees = 60.0f;
    internal static readonly Vector3 LocalPosition = new(12.0f, 8.0f, 32.0f);
    internal static readonly Vector3 LocalTarget = new(0.0f, -9.0f, -8.0f);
}
