using System;
using System.Collections.Generic;
using FantaSim.App.World;
using Godot;

namespace FantaSim.App.Timeline.Seam;

internal readonly record struct FilmstripFrameMetadata(
    string SphereId,
    string LayerId,
    long RequestedTick,
    long SnapshotTick,
    string ViewRung,
    int SourceFrequency,
    int Width,
    int Height,
    string SourceKind,
    int GraphRevision);

internal readonly record struct FilmstripFramePayload(
    ImageTexture Texture,
    FilmstripFrameMetadata Metadata);

internal static class FilmstripFramePayloadPolicy
{
    internal static string RequestKey(LayerFilmstripPreviewRequest request)
        => $"{request.SphereId}:{request.LayerId}:{request.Tick}:{request.ViewRung}:{request.Width}x{request.Height}:r{request.GraphRevision}";

    internal static FilmstripTextureCacheKey CacheKey(LayerFilmstripPreviewMap map)
        => new(
            map.SphereId,
            map.LayerId,
            map.RequestedTick,
            map.SnapshotTick,
            map.ViewRung,
            map.Width,
            map.Height,
            map.GraphRevision);

    internal static bool Matches(
        LayerFilmstripPreviewMap map,
        LayerFilmstripPreviewRequest request)
        => string.Equals(map.SphereId, request.SphereId, StringComparison.Ordinal)
           && string.Equals(map.LayerId, request.LayerId, StringComparison.Ordinal)
           && map.RequestedTick == request.Tick
           && string.Equals(map.ViewRung, request.ViewRung, StringComparison.Ordinal)
           && map.Width == request.Width
           && map.Height == request.Height
           && map.GraphRevision == request.GraphRevision;

    internal static FilmstripFramePayload Build(
        ImageTexture texture,
        LayerFilmstripPreviewMap map)
        => new(texture, new FilmstripFrameMetadata(
            map.SphereId,
            map.LayerId,
            map.RequestedTick,
            map.SnapshotTick,
            map.ViewRung,
            map.SourceFrequency,
            map.Width,
            map.Height,
            map.SourceKind,
            map.GraphRevision));

    internal static bool TryGetCached(
        LayerFilmstripPreviewRequest request,
        IReadOnlyDictionary<string, FilmstripTextureCacheKey> requestKeys,
        IReadOnlyDictionary<FilmstripTextureCacheKey, FilmstripFramePayload> cache,
        out FilmstripFramePayload payload)
    {
        var requestKey = RequestKey(request);
        if (requestKeys.TryGetValue(requestKey, out var cacheKey)
            && cache.TryGetValue(cacheKey, out payload))
            return true;

        payload = default;
        return false;
    }
}

/// <summary>Where a fetched filmstrip payload lands. The 2D adapter intentionally consumes only
/// the texture; 3D adapters may use provenance metadata to enforce an honest source policy.</summary>
internal interface IFilmstripFrameSink
{
    bool IsAlive { get; }
    void SetFrame(FilmstripFramePayload frame);
}

internal sealed class TextureRectFilmstripSink : IFilmstripFrameSink
{
    private readonly TextureRect _textureRect;
    public TextureRectFilmstripSink(TextureRect textureRect) => _textureRect = textureRect;
    public bool IsAlive => GodotObject.IsInstanceValid(_textureRect) && _textureRect.IsInsideTree();
    public void SetFrame(FilmstripFramePayload frame) => _textureRect.Texture = frame.Texture;
}
