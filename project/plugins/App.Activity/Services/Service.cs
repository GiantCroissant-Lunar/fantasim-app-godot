using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrosscutFoundation.Messaging;
using Microsoft.Extensions.Logging;
using UnifyStorage.Abstractions;
using UnifyStorage.Runtime.LiteDb;

[assembly: InternalsVisibleTo("App.Activity.Tests")]
[assembly: InternalsVisibleTo("FantaSim.App.Activity.Tests")]

namespace FantaSim.App.Activity.Services;

/// <summary>
/// The activity-ledger service (<see cref="IService"/>) - append-only with optional persistence.
/// Subscribes to <see cref="ActivityEntry"/> on the crosscut bus and records every entry;
/// <see cref="Append"/> is the direct path. Capped at the configured entry count (ring-buffer semantics:
/// when full, the oldest entry is evicted). Thread-safe via an internal lock.
/// </summary>
public sealed class Service : IService, IDisposable
{
    private const string DocumentCollection = "activityLedger";
    private const string DocumentId = "activity-ledger.latest";
    private const int DefaultMaxCapacity = 10_000;

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageBus _bus;
    private readonly ILogger _log;
    private readonly int _maxCapacity;
    private readonly IDocumentStore? _documentStore;
    private readonly IDisposable? _ownedDocumentStore;
    private readonly List<ActivityEntry> _entries = new();
    private readonly object _lock = new();

    private readonly SemaphoreSlim _flushSignal = new(0);
    private readonly CancellationTokenSource? _flushCts;
    private readonly Task? _flushWorker;
    private int _dirty;
    private long _appendSequence;
    private long _persistedSequence;

    private readonly IDisposable _busSubscription;
    private bool _disposed;

    /// <summary>
    /// Debounce interval applied by the background flusher. Configurable so tests can flush
    /// deterministically by setting this to <see cref="TimeSpan.Zero"/>.
    /// </summary>
    internal TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public Service(IMessageBus bus, ILoggerFactory loggerFactory, ActivityOptions? options = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _log = loggerFactory.CreateLogger("App.Activity.Service");
        var effectiveOptions = options ?? new ActivityOptions();
        _maxCapacity = Math.Max(1, effectiveOptions.MaxEntries == 0 ? DefaultMaxCapacity : effectiveOptions.MaxEntries);

        (_documentStore, _ownedDocumentStore) = CreateDocumentStore(effectiveOptions, _log);
        LoadPersistedEntries();

        if (_documentStore is not null)
        {
            _flushCts = new CancellationTokenSource();
            _flushWorker = Task.Run(() => FlushWorkerAsync(_flushCts.Token));
        }

        _busSubscription = _bus.Subscribe<ActivityEntry>(Append);
    }

    public void Append(ActivityEntry entry)
    {
        if (entry is null)
        {
            _log.LogWarning("Append called with a null entry; ignored.");
            return;
        }

        lock (_lock)
        {
            if (_entries.Count >= _maxCapacity)
                _entries.RemoveAt(0);

            _entries.Add(entry);
            _appendSequence++;
        }

        if (_documentStore is not null && Interlocked.Exchange(ref _dirty, 1) == 0)
            _flushSignal.Release();

        _log.LogDebug(
            "Activity appended: Kind={Kind}, Name={Name}, CorrelationId={CorrelationId}.",
            entry.Kind, entry.Name, entry.CorrelationId);
    }

    public IReadOnlyList<ActivityEntry> QueryLatest(int count)
    {
        if (count <= 0)
            return Array.Empty<ActivityEntry>();

        lock (_lock)
        {
            int skip = Math.Max(0, _entries.Count - count);
            return _entries.Skip(skip).Reverse().ToArray();
        }
    }

    public IReadOnlyList<ActivityEntry> QueryByCorrelationId(string correlationId)
    {
        if (string.IsNullOrEmpty(correlationId))
            return Array.Empty<ActivityEntry>();

        lock (_lock)
        {
            return _entries
                .Where(e => e.CorrelationId == correlationId)
                .ToArray();
        }
    }

    /// <summary>
    /// Waits until every entry appended before this call has been persisted. Persist cycles are
    /// tracked by append sequence number, so a flush already in flight with a stale snapshot
    /// does not satisfy the wait. Exposed for deterministic testing; not a public API.
    /// </summary>
    internal async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_documentStore is null)
            return;

        long target;
        lock (_lock)
        {
            target = _appendSequence;
        }

        if (Interlocked.Read(ref _persistedSequence) >= target)
            return;

        if (Interlocked.Exchange(ref _dirty, 1) == 0)
            _flushSignal.Release();

        while (Interlocked.Read(ref _persistedSequence) < target)
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _busSubscription.Dispose();

        if (_flushWorker is not null)
        {
            _flushCts!.Cancel();
            try
            {
                _flushWorker.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Expected when the cancellation token stops the worker.
            }
            _flushCts.Dispose();
            _flushSignal.Dispose();
        }

        if (_documentStore is not null && _dirty == 1)
            FlushOnceAsync().GetAwaiter().GetResult();

        _ownedDocumentStore?.Dispose();
    }

    private static (IDocumentStore? Store, IDisposable? OwnedStore) CreateDocumentStore(
        ActivityOptions options,
        ILogger log)
    {
        if (!options.PersistActivityLedger)
            return (null, null);

        var storePath = ResolveStorePath(options.ActivityLedgerStorePath);
        try
        {
            var directory = Path.GetDirectoryName(storePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var store = new LiteDbDocumentStore(storePath);
            log.LogInformation("Activity ledger persistence enabled at {Path}.", storePath);
            return (store, store);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to create activity-ledger persistence store at {Path}; persistence disabled.", storePath);
            return (null, null);
        }
    }

    private static string ResolveStorePath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? "user://activity-ledger.litedb"
            : configuredPath.Trim();

        const string UserPrefix = "user://";
        if (path.StartsWith(UserPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = AppContext.BaseDirectory;

            var relative = path[UserPrefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return Path.GetFullPath(Path.Combine(localAppData, "FantaSim", "complete-app", relative));
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private void LoadPersistedEntries()
    {
        if (_documentStore is null)
            return;

        try
        {
            var stored = _documentStore.GetAsync<DocumentPayload>(DocumentCollection, DocumentId)
                .GetAwaiter()
                .GetResult();
            if (stored is null || stored.Data.Length == 0)
            {
                _log.LogInformation("No persisted activity ledger found; starting empty.");
                return;
            }

            var document = JsonSerializer.Deserialize<ActivityLedgerDocument>(stored.Data, s_jsonOptions);
            if (document?.Entries is null)
            {
                _log.LogWarning("Persisted activity ledger was empty or malformed; starting empty.");
                return;
            }

            var entries = TrimToCapacity(document.Entries);
            lock (_lock)
            {
                _entries.Clear();
                _entries.AddRange(entries);
            }

            _log.LogInformation("Loaded {Count} persisted activity entries.", entries.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load persisted activity ledger; starting empty.");
        }
    }

    private async Task FlushWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    await _flushSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (FlushInterval > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(FlushInterval, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                await FlushOnceAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Activity ledger flush worker failed unexpectedly.");
        }
    }

    private async Task FlushOnceAsync()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0)
            return;

        List<ActivityEntry> snapshot;
        long snapshotSequence;
        lock (_lock)
        {
            snapshot = _entries.ToList();
            snapshotSequence = _appendSequence;
        }

        await TryPersistSnapshotAsync(snapshot).ConfigureAwait(false);

        long observed;
        while ((observed = Interlocked.Read(ref _persistedSequence)) < snapshotSequence
               && Interlocked.CompareExchange(ref _persistedSequence, snapshotSequence, observed) != observed)
        {
        }
    }

    private async Task TryPersistSnapshotAsync(IReadOnlyList<ActivityEntry> entries)
    {
        if (_documentStore is null)
            return;

        try
        {
            var document = new ActivityLedgerDocument
            {
                SchemaVersion = 1,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Entries = entries.ToList(),
            };
            var payload = JsonSerializer.SerializeToUtf8Bytes(document, s_jsonOptions);
            await _documentStore.UpsertAsync(DocumentCollection, DocumentId, new DocumentPayload(payload), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist activity ledger snapshot.");
        }
    }

    private List<ActivityEntry> TrimToCapacity(IReadOnlyList<ActivityEntry> entries)
    {
        if (entries.Count <= _maxCapacity)
            return entries.ToList();

        return entries.Skip(entries.Count - _maxCapacity).ToList();
    }
}

internal sealed class ActivityLedgerDocument
{
    public int SchemaVersion { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<ActivityEntry> Entries { get; set; } = new();
}

internal sealed class DocumentPayload
{
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public DocumentPayload()
    {
    }

    public DocumentPayload(byte[] data) => Data = data;
}
