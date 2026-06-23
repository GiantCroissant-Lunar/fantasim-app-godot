namespace FantaSim.App.Activity;

/// <summary>
/// Runtime options for the activity ledger service.
/// </summary>
/// <param name="PersistActivityLedger">Persist the in-memory ledger to a document store.</param>
/// <param name="ActivityLedgerStorePath">LiteDB store path. <c>user://</c> maps to local app data.</param>
/// <param name="MaxEntries">Maximum retained ledger entries; oldest entries are evicted first.</param>
public sealed record ActivityOptions(
    bool PersistActivityLedger = false,
    string ActivityLedgerStorePath = "user://activity-ledger.litedb",
    int MaxEntries = 10_000);
