using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace FantaSim.App.Iii.Seam;

/// <summary>
/// Bundle-backed <see cref="IIiiWorkerCatalog"/> that reads workers.json from a loaded iii bundle.
/// Refresh is called on initial load and again whenever the resource runtime signals a change
/// (e.g. hot-reload of the iii PCK).
/// </summary>
public sealed class IiiWorkerBundleCatalog : IIiiWorkerCatalog
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private IiiWorkerManifest _manifest = IiiWorkerManifest.Default;

    public IReadOnlyList<IiiWorkerDefinition> Workers => _manifest.Workers;

    public event EventHandler? Changed;

    /// <summary>
    /// Reads or re-reads workers.json from <c>res://bundles/iii/workers.json</c>.
    /// If the file is missing, falls back to <see cref="IiiWorkerManifest.Default"/>.
    /// </summary>
    public void Refresh()
    {
        const string workersResPath = "res://bundles/iii/workers.json";

        if (!Godot.FileAccess.FileExists(workersResPath))
        {
            _manifest = IiiWorkerManifest.Default;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        using var file = Godot.FileAccess.Open(workersResPath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            _manifest = IiiWorkerManifest.Default;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var json = file.GetAsText();
        _manifest = IiiWorkerManifest.FromJson(json) ?? IiiWorkerManifest.Default;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
