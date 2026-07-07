using Godot;

namespace FantaSim.App.Timeline.Seam;

internal sealed partial class TimelinePlayheadHandle : Control
{
    public override void _Draw()
    {
        var w = Size.X;
        var h = Size.Y;
        var c = w / 2f;
        var fill = new Color(0.36f, 0.78f, 1.0f, 0.96f);
        var edge = new Color(1.0f, 1.0f, 1.0f, 0.86f);

        var points = new[]
        {
            new Vector2(c, 2f),
            new Vector2(w - 4f, 9f),
            new Vector2(c + 4f, 9f),
            new Vector2(c + 4f, h - 2f),
            new Vector2(c - 4f, h - 2f),
            new Vector2(c - 4f, 9f),
            new Vector2(4f, 9f)
        };

        DrawColoredPolygon(points, fill);
        for (int i = 0; i < points.Length; i++)
        {
            DrawLine(points[i], points[(i + 1) % points.Length], edge, 1f, antialiased: true);
        }
    }
}
