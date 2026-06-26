using System;
using System.Collections.Generic;

namespace FantaSim.App.Iii;

/// <summary>
/// Read-only catalog of iii worker definitions. Implementations may refresh from a
/// reloadable bundle; consumers such as <see cref="IiiFunctionProvider"/> should read
/// <see cref="Workers"/> on each call rather than caching the list.
/// </summary>
public interface IIiiWorkerCatalog
{
    IReadOnlyList<IiiWorkerDefinition> Workers { get; }

    event EventHandler? Changed;
}
