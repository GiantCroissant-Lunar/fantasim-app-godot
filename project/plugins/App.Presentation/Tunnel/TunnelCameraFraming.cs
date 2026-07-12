using System.Numerics;

namespace FantaSim.App.Presentation.Tunnel;

// Interior occupant pose (design §4a): on-axis at the mouth looking down -Z at the globe on the
// current-tick plane, so the planet reads large at the center with the wall receding around it.
internal static class TunnelCameraFraming
{
    internal const float FieldOfViewDegrees = 60.0f;
    internal static readonly Vector3 LocalPosition = new(0.0f, 0.6f, 2.2f);
    internal static readonly Vector3 LocalTarget = new(0.0f, 0.0f, -5.0f);
}
