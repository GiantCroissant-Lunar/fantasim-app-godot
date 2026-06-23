using System.Collections.Generic;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Activity;

/// <summary>
/// T1 activity-ledger contract (in-memory, append-only). Recording is normally bus-driven -
/// publishers publish <see cref="ActivityEntry"/> on the crosscut IMessageBus and the T3
/// subscribes and appends; <see cref="Append"/> is the direct path.
/// </summary>
[ServiceContract]
[SelectionStrategy(SelectionMode.HighestPriority)]
public interface IService
{
    /// <summary>Append an entry directly to the ledger.</summary>
    void Append(ActivityEntry entry);

    /// <summary>Query the latest <paramref name="count"/> entries.</summary>
    IReadOnlyList<ActivityEntry> QueryLatest(int count);

    /// <summary>Query all entries sharing the given <paramref name="correlationId"/>.</summary>
    IReadOnlyList<ActivityEntry> QueryByCorrelationId(string correlationId);
}
