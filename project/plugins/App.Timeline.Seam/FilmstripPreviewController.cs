using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using FantaSim.App.World;
using FantaSim.App.Timeline.Providers;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Extracted filmstrip preview machinery: manages the texture cache, request queue, in-flight
/// bookkeeping, and generation counter for the timeline face's filmstrip rendering. Godot-aware
/// (holds ImageTexture references) but does not inherit from Node; the face owns the lifecycle.
///
/// The 825461b disposed-guard fix is preserved verbatim: the deferred callable bails before any
/// Godot call when the face is gone, and ApplyFilmstripPreview checks aliveness before touching
/// the tree. The aliveness predicate is injected via constructor so the controller never touches
/// the face directly.
/// Split from TimelineFace 2026-07-11 (vault/plans/2026-07-11-timelineface-split-plan.md).
/// </summary>
internal sealed class FilmstripPreviewController : IDisposable
{
    private readonly Func<bool> _isFaceAlive;
    private readonly Action<Action> _deferToMainThread;
    private readonly ILogger _log;

    // Preview provider — read at EXECUTION time (ALC rule: never capture the Func in a closure
    // that outlives the bundle; reading the field at call time means sever sets it to null and
    // in-flight Task reads null and skips). Set by the face at bind/clear time.
    private Func<LayerFilmstripPreviewRequest, CancellationToken, LayerFilmstripPreviewMap?>? _filmstripPreviewProvider;

    private readonly Dictionary<FilmstripTextureCacheKey, ImageTexture> _filmstripTextureCache = new();
    private readonly FilmstripCacheLedger _cacheLedger;
    private readonly Dictionary<string, FilmstripTextureCacheKey> _ledgerKeyToCacheKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FilmstripTextureCacheKey> _filmstripRequestTextureKeys = new(StringComparer.Ordinal);
    private readonly Queue<QueuedFilmstripFrame> _filmstripQueue = new();
    private readonly HashSet<string> _filmstripQueuedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _filmstripActiveKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PendingFilmstripFrame>> _filmstripWaiters = new(StringComparer.Ordinal);
    private CancellationTokenSource _filmstripCts = new();
    private int _filmstripActiveRequests;
    private int _filmstripGeneration;

    private const int MaxConcurrentFilmstripRequests = 3;
    private const int MaxFilmstripTextureCacheEntries = 512;
    private const float TrackHeight = TimelineFilmstrip.CompactTrackHeight;

    internal sealed record PendingFilmstripFrame(int Generation, IFilmstripFrameSink Sink);

    internal sealed record QueuedFilmstripFrame(
        LayerFilmstripPreviewRequest Request,
        string RequestKey,
        int GraphRevision);

    public FilmstripPreviewController(
        Func<bool> isFaceAlive,
        Action<Action> deferToMainThread,
        ILogger log)
    {
        _isFaceAlive = isFaceAlive;
        _deferToMainThread = deferToMainThread;
        _log = log;
        _cacheLedger = new FilmstripCacheLedger(MaxFilmstripTextureCacheEntries);
    }

    public void SetPreviewProvider(Func<LayerFilmstripPreviewRequest, CancellationToken, LayerFilmstripPreviewMap?>? provider)
        => _filmstripPreviewProvider = provider;

    public void CancelInFlight()
    {
        // Unwind IN-FLIGHT renders: their threadpool stacks execute bundle code and would root
        // the outgoing timeline+world ALCs past the collection probe (mechanism C,
        // clrstack-proven 2026-07-10). The world render honors the token per pixel row.
        _filmstripCts.Cancel();
        _filmstripCts.Dispose();
        _filmstripCts = new CancellationTokenSource();
    }

    public static Control BuildFramePlaceholder(TimelineFilmstripFrameSlot slot, bool activeAtSlot)
    {
        var frame = new PanelContainer
        {
            Name = $"Frame_{slot.Index}",
            Position = new Vector2(slot.X, 0f),
            Size = new Vector2(slot.Width, TrackHeight),
            CustomMinimumSize = new Vector2(slot.Width, TrackHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.11f, 0.12f, 0.86f),
            BorderColor = new Color(0.24f, 0.30f, 0.34f, 0.82f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(0);
        frame.AddThemeStyleboxOverride("panel", style);

        var texture = new TextureRect
        {
            Name = "Texture",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Modulate = activeAtSlot ? Colors.White : new Color(1f, 1f, 1f, 0.58f),
        };
        texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        frame.AddChild(texture);
        return frame;
    }

    public void RequestTexture(
        IFilmstripFrameSink sink,
        string sphere,
        string layerId,
        long tick,
        string rung,
        int graphRevision)
    {
        var provider = _filmstripPreviewProvider;
        if (provider is null)
            return;

        var request = new LayerFilmstripPreviewRequest(
            sphere,
            layerId,
            tick,
            rung,
            TimelineFilmstrip.ThumbnailWidth,
            TimelineFilmstrip.ThumbnailHeight);
        string requestKey = FilmstripRequestKey(request);
        if (_filmstripRequestTextureKeys.TryGetValue(requestKey, out var cachedKey)
            && _filmstripTextureCache.TryGetValue(cachedKey, out var cachedTexture)
            && GodotObject.IsInstanceValid(cachedTexture))
        {
            sink.SetTexture(cachedTexture);
            return;
        }

        // Hold the sink reference directly: the frame is built BEFORE the track row
        // enters the tree, so GetPath() errors and NodePath resolution would be impossible here
        // (windowed gate 2026-07-08). Sink.IsAlive at apply time is the guard.
        var generation = _filmstripGeneration;
        if (!_filmstripWaiters.TryGetValue(requestKey, out var waiters))
        {
            waiters = new List<PendingFilmstripFrame>();
            _filmstripWaiters[requestKey] = waiters;
        }

        waiters.Add(new PendingFilmstripFrame(generation, sink));
        if (_filmstripActiveKeys.Contains(requestKey) || !_filmstripQueuedKeys.Add(requestKey))
            return;

        _filmstripQueue.Enqueue(new QueuedFilmstripFrame(request, requestKey, graphRevision));
        PumpFilmstripQueue();
    }

    private void PumpFilmstripQueue()
    {
        if (_filmstripPreviewProvider is null)
            return;

        while (_filmstripActiveRequests < MaxConcurrentFilmstripRequests && _filmstripQueue.Count > 0)
        {
            var queued = _filmstripQueue.Dequeue();
            _filmstripQueuedKeys.Remove(queued.RequestKey);
            PruneFilmstripWaiters(queued.RequestKey);
            if (!_filmstripWaiters.ContainsKey(queued.RequestKey))
            {
                continue;
            }

            _filmstripActiveRequests++;
            _filmstripActiveKeys.Add(queued.RequestKey);
            StartFilmstripRequest(queued);
        }
    }

    private void StartFilmstripRequest(QueuedFilmstripFrame queued)
    {
        var cancellationToken = _filmstripCts.Token;
        _ = Task.Run(() =>
        {
            LayerFilmstripPreviewMap? map = null;
            try
            {
                // Resolve the provider at EXECUTION time, never at capture time: the Func is
                // compiled into the timeline bundle, so a captured copy sitting in this Task (or
                // the completion callable below, GCHandle-held in Godot's deferred queue) roots
                // the outgoing generation's ALC for as long as the closure lives. Under load the
                // filmstrip renders outlive the 32-frame collection probe and the gate reports
                // "old ALC still pinned for bundle timeline" (dump-proven 2026-07-10:
                // TimelineFace+<>c__DisplayClass140_0 -> provider Func -> old LoaderAllocator).
                // Reading the field means ClearResidentContext's sever instantly drops the old
                // generation. The token (cancelled at sever) unwinds a render already on this
                // stack, so even the in-flight window ends within a pixel row.
                var provider = _filmstripPreviewProvider;
                if (provider is not null && !cancellationToken.IsCancellationRequested)
                    map = provider(queued.Request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Severed mid-render (bundle reload): drop the frame quietly — the next bind's
                // BuildLanes re-requests every visible slot.
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex,
                    "Timeline filmstrip preview failed for {LayerId} at {Tick}.",
                    queued.Request.LayerId,
                    queued.Request.Tick);
            }

            _deferToMainThread(() =>
            {
                // The deferred queue outlives the face during multi-bundle reload waves: Godot
                // frees the node before this callable fires, so the first native call
                // (ApplyFilmstripPreview's IsInsideTree) throws ObjectDisposedException through
                // the trampoline — teardown noise, not a pin (the ALC still collects). Bail
                // before any Godot call; the bookkeeping below dies with the instance anyway.
                if (!_isFaceAlive())
                    return;

                _filmstripActiveRequests = Math.Max(0, _filmstripActiveRequests - 1);
                _filmstripActiveKeys.Remove(queued.RequestKey);
                if (map is not null)
                    ApplyFilmstripPreview(queued.RequestKey, map, queued.GraphRevision);
                _filmstripWaiters.Remove(queued.RequestKey);
                PumpFilmstripQueue();
            });
        });
    }

    private static string FilmstripRequestKey(LayerFilmstripPreviewRequest request)
        => $"{request.SphereId}:{request.LayerId}:{request.Tick}:{request.ViewRung}:{request.Width}x{request.Height}";

    private void ApplyFilmstripPreview(string requestKey, LayerFilmstripPreviewMap map, int graphRevision)
    {
        // IsInstanceValid first: IsInsideTree is a native call and throws on a disposed face.
        if (!_isFaceAlive())
            return;

        if (!_filmstripWaiters.TryGetValue(requestKey, out var waiters))
            return;

        // GraphRevision completes the world-identity gap (2026-07-11 cache-key completion,
        // vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md §1.3): see
        // FilmstripTextureCacheKey's doc comment for what IS/is-not reachable here and why (Seed
        // is residue -- not reachable without a T1 contract change).
        var key = new FilmstripTextureCacheKey(
            map.SphereId,
            map.LayerId,
            map.SnapshotTick,
            map.ViewRung,
            map.Width,
            map.Height,
            graphRevision);
        var isNewKey = !_filmstripTextureCache.ContainsKey(key);
        if (!_filmstripTextureCache.TryGetValue(key, out var texture)
            || !GodotObject.IsInstanceValid(texture))
        {
            var image = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rgba8, map.Rgba32);
            texture = ImageTexture.CreateFromImage(image);
            _filmstripTextureCache[key] = texture;
        }

        if (isNewKey)
        {
            var ledgerKey = key.ToString();
            var evictedKey = _cacheLedger.Record(ledgerKey);
            _ledgerKeyToCacheKey[ledgerKey] = key;
            if (evictedKey is not null)
            {
                if (_ledgerKeyToCacheKey.TryGetValue(evictedKey, out var evictedCacheKey))
                {
                    if (_filmstripTextureCache.TryGetValue(evictedCacheKey, out var evicted)
                        && GodotObject.IsInstanceValid(evicted))
                        evicted.Dispose();
                    _filmstripTextureCache.Remove(evictedCacheKey);
                    _ledgerKeyToCacheKey.Remove(evictedKey);
                }
            }
        }

        _filmstripRequestTextureKeys[requestKey] = key;
        foreach (var pending in waiters)
        {
            if (pending.Generation != _filmstripGeneration)
                continue;

            if (!pending.Sink.IsAlive)
                continue;

            pending.Sink.SetTexture(texture);
        }
    }

    private void PruneFilmstripWaiters(string requestKey)
    {
        if (!_filmstripWaiters.TryGetValue(requestKey, out var waiters))
            return;

        waiters.RemoveAll(pending => pending.Generation != _filmstripGeneration);
        if (waiters.Count == 0)
            _filmstripWaiters.Remove(requestKey);
    }

    public void Supersede()
    {
        _filmstripGeneration++;
        _filmstripQueue.Clear();
        _filmstripQueuedKeys.Clear();
        foreach (var requestKey in _filmstripWaiters.Keys.ToArray())
            PruneFilmstripWaiters(requestKey);
    }

    public void DisposeCache()
    {
        foreach (var texture in _filmstripTextureCache.Values)
        {
            if (GodotObject.IsInstanceValid(texture))
                texture.Dispose();
        }

        _filmstripTextureCache.Clear();
        _cacheLedger.Clear();
        _ledgerKeyToCacheKey.Clear();
        _filmstripRequestTextureKeys.Clear();
        _filmstripQueue.Clear();
        _filmstripQueuedKeys.Clear();
        _filmstripActiveKeys.Clear();
        _filmstripWaiters.Clear();
        _filmstripActiveRequests = 0;
    }

    public void Dispose()
    {
        _filmstripCts.Cancel();
        _filmstripCts.Dispose();
    }
}