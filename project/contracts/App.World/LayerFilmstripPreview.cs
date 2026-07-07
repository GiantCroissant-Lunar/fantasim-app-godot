using System;
using System.Collections.Generic;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World;

public sealed record LayerFilmstripPreviewRequest(
    string SphereId,
    string LayerId,
    long Tick,
    string ViewRung,
    int Width = 96,
    int Height = 48);

public sealed record LayerFilmstripPreviewMap(
    string SphereId,
    string LayerId,
    long RequestedTick,
    long SnapshotTick,
    string ViewRung,
    int SourceFrequency,
    int Width,
    int Height,
    string SourceKind,
    byte[] Rgba32);

public readonly record struct LayerFilmstripCellSample(
    int CellId,
    float X,
    float Y,
    float Z,
    int PlateId,
    double Primary,
    double Secondary);

public static class LayerFilmstripEquirect
{
    public static GlobeVec3 PixelToDirection(int x, int y, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if ((uint)x >= (uint)width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)height) throw new ArgumentOutOfRangeException(nameof(y));

        double u = (x + 0.5) / width;
        double v = (y + 0.5) / height;
        double lon = (u * Math.PI * 2.0) - Math.PI;
        double lat = (Math.PI * 0.5) - (v * Math.PI);
        double cosLat = Math.Cos(lat);

        return new GlobeVec3(
            (float)(cosLat * Math.Cos(lon)),
            (float)(cosLat * Math.Sin(lon)),
            (float)Math.Sin(lat));
    }

    public static int NearestCellIndex(GlobeVec3 direction, IReadOnlyList<LayerFilmstripCellSample> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Count == 0)
            return -1;

        double bestDot = double.NegativeInfinity;
        int best = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            double dot = direction.X * cell.X + direction.Y * cell.Y + direction.Z * cell.Z;
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        return best;
    }
}
