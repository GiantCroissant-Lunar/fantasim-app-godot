using System;
using System.Collections.Generic;

namespace FantaSim.App.Iii;

/// <summary>
/// In-memory <see cref="IIiiWorkerCatalog"/> backed by an <see cref="IiiWorkerManifest"/>.
/// </summary>
public sealed class IiiWorkerCatalog : IIiiWorkerCatalog
{
    private IiiWorkerManifest _manifest;

    public IiiWorkerCatalog(IiiWorkerManifest? manifest = null)
    {
        _manifest = manifest ?? IiiWorkerManifest.Default;
    }

    public IReadOnlyList<IiiWorkerDefinition> Workers => _manifest.Workers;

    public event EventHandler? Changed;

    public void Update(IiiWorkerManifest manifest)
    {
        _manifest = manifest ?? IiiWorkerManifest.Default;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
