using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FantaSim.App.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantaSim.App.World.Composition;

/// <summary>
/// T3 orchestrator for <see cref="ILayerTrackRegistry"/>: reads the pipeline json + declared-
/// layers json from disk, asks the caller for the current generation family document, and
/// interprets them through the pure <see cref="LayerTrackRegistryBuilder"/>. The archive overlay
/// (which tracks are user-hidden) persists to a small sidecar json so it survives an app restart;
/// the underlying declared/discovered data itself is never deleted -- only the overlay flag.
/// </summary>
/// <remarks>
/// Composed and owned by <c>FantaSim.App.Timeline.TimelinePlugin</c> (the timeline category owns
/// track UX -- vault/plans/2026-07-10-layer-track-registry-slice1-plan.md Task 3), the same way
/// that plugin already owns the timeline <c>Services.Service</c> instance. All paths are
/// injected, never resolved internally, so this class stays testable against a temp directory.
/// </remarks>
public sealed class LayerTrackRegistryService : ILayerTrackRegistry, IDisposable
{
    private static readonly JsonSerializerOptions AssetReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly Func<WorldGenerationGraphFamilyDocument?> _familyDocumentProvider;
    private readonly string _pipelineJsonPath;
    private readonly string _declaredLayersJsonPath;
    private readonly string _archiveOverlayPath;
    private readonly ILogger _log;
    private readonly HashSet<string> _archivedKeys = new(StringComparer.Ordinal);

    private LayerTrackRegistrySnapshot _current;
    private int _revision;
    private bool _disposed;

    public LayerTrackRegistryService(
        Func<WorldGenerationGraphFamilyDocument?> familyDocumentProvider,
        string pipelineJsonPath,
        string declaredLayersJsonPath,
        string archiveOverlayPath,
        ILoggerFactory? loggerFactory = null)
    {
        _familyDocumentProvider = familyDocumentProvider ?? throw new ArgumentNullException(nameof(familyDocumentProvider));
        _pipelineJsonPath = pipelineJsonPath ?? throw new ArgumentNullException(nameof(pipelineJsonPath));
        _declaredLayersJsonPath = declaredLayersJsonPath ?? throw new ArgumentNullException(nameof(declaredLayersJsonPath));
        _archiveOverlayPath = archiveOverlayPath ?? throw new ArgumentNullException(nameof(archiveOverlayPath));
        _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("LayerTrackRegistry");

        LoadArchiveOverlay();
        _current = BuildSnapshot();
    }

    public LayerTrackRegistrySnapshot Current
    {
        get { lock (_gate) return _current; }
    }

    public event Action<LayerTrackRegistrySnapshot>? Changed;

    public void SetArchived(string sphereId, string layerId, bool archived)
    {
        if (string.IsNullOrWhiteSpace(sphereId))
            throw new ArgumentException("sphereId is required.", nameof(sphereId));
        if (string.IsNullOrWhiteSpace(layerId))
            throw new ArgumentException("layerId is required.", nameof(layerId));

        var key = LayerTrackRegistryBuilder.ArchiveKey(sphereId, layerId);
        LayerTrackRegistrySnapshot snapshot;
        bool mutated;
        lock (_gate)
        {
            mutated = archived ? _archivedKeys.Add(key) : _archivedKeys.Remove(key);
            if (!mutated)
                return;

            SaveArchiveOverlay();
            _current = BuildSnapshotLocked();
            snapshot = _current;
        }

        Changed?.Invoke(snapshot);
    }

    public void Reload()
    {
        LayerTrackRegistrySnapshot snapshot;
        lock (_gate)
        {
            _current = BuildSnapshotLocked();
            snapshot = _current;
        }

        Changed?.Invoke(snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Changed = null;
    }

    private LayerTrackRegistrySnapshot BuildSnapshot()
    {
        lock (_gate)
            return BuildSnapshotLocked();
    }

    private LayerTrackRegistrySnapshot BuildSnapshotLocked()
    {
        var pipeline = ReadPipelineDocument();
        var declaredLayers = ReadDeclaredLayersDocument();
        WorldGenerationGraphFamilyDocument? family = null;
        try
        {
            family = _familyDocumentProvider();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LayerTrackRegistry: family-document provider threw; family-layers source will be empty.");
        }

        _revision++;
        return LayerTrackRegistryBuilder.Build(pipeline, family, declaredLayers, _archivedKeys, _revision);
    }

    private TrackPipelineDocument ReadPipelineDocument()
    {
        var json = TryReadFile(_pipelineJsonPath);
        if (json is null)
        {
            _log.LogWarning("LayerTrackRegistry: pipeline json not found at {Path}; registry will be empty.", _pipelineJsonPath);
            return new TrackPipelineDocument("track-pipeline.empty", SchemaVersion: 1, Revision: 0, Nodes: Array.Empty<TrackPipelineNode>(), Wires: Array.Empty<TrackPipelineWire>());
        }

        try
        {
            return JsonSerializer.Deserialize<TrackPipelineDocument>(json, AssetReadOptions)
                ?? throw new InvalidOperationException($"Pipeline json at {_pipelineJsonPath} deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse track-pipeline json at {_pipelineJsonPath}: {ex.Message}", ex);
        }
    }

    private DeclaredLayersDocument? ReadDeclaredLayersDocument()
    {
        var json = TryReadFile(_declaredLayersJsonPath);
        if (json is null)
        {
            _log.LogWarning("LayerTrackRegistry: declared-layers json not found at {Path}; no declared-only tracks.", _declaredLayersJsonPath);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeclaredLayersDocument>(json, AssetReadOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse declared-layers json at {_declaredLayersJsonPath}: {ex.Message}", ex);
        }
    }

    private void LoadArchiveOverlay()
    {
        var json = TryReadFile(_archiveOverlayPath);
        if (json is null)
            return;

        try
        {
            var overlay = JsonSerializer.Deserialize<ArchiveOverlayDocument>(json, AssetReadOptions);
            if (overlay?.ArchivedKeys is null)
                return;

            foreach (var key in overlay.ArchivedKeys)
                _archivedKeys.Add(key);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "LayerTrackRegistry: failed to parse archive overlay at {Path}; starting with none archived.", _archiveOverlayPath);
        }
    }

    private void SaveArchiveOverlay()
    {
        try
        {
            var directory = Path.GetDirectoryName(_archiveOverlayPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var overlay = new ArchiveOverlayDocument(new List<string>(_archivedKeys));
            File.WriteAllText(_archiveOverlayPath, JsonSerializer.Serialize(overlay, AssetReadOptions));
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "LayerTrackRegistry: failed to persist archive overlay to {Path}; archive state is in-memory only for this session.", _archiveOverlayPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.LogWarning(ex, "LayerTrackRegistry: not permitted to persist archive overlay to {Path}; archive state is in-memory only for this session.", _archiveOverlayPath);
        }
    }

    private static string? TryReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record ArchiveOverlayDocument(IReadOnlyList<string> ArchivedKeys);
}
