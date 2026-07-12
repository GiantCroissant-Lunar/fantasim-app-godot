namespace FantaSim.App.Activity;

/// <summary>Kinds of activity entries recorded in the ledger.</summary>
public enum ActivityEntryKind
{
    UiOperation,
    DomainCommand,
    CommandResult,
    Log,
}

/// <summary>
/// The actor that caused an activity entry.
/// <paramref name="Kind"/> is "user" | "cli" | "tui" | "agent".
/// </summary>
public sealed record ActivityActor(string Kind, string? Id = null);

/// <summary>
/// A single entry in the activity ledger. Entries are published on the crosscut
/// <c>IMessageBus</c> and recorded by the <c>App.Activity</c> T3 ledger.
/// </summary>
/// <param name="EntryId">Unique instance id (GUID string).</param>
/// <param name="Kind">The kind of activity entry.</param>
/// <param name="Timestamp">When the entry occurred.</param>
/// <param name="Actor">Who caused the entry.</param>
/// <param name="Name">The command/operation id (e.g. "view.show").</param>
/// <param name="Category">The descriptor category (e.g. "view", "input", "compound").</param>
/// <param name="PayloadJson">Optional JSON payload.</param>
/// <param name="CausationId">
/// The <see cref="EntryId"/> of the parent entry (compound/shortcut chains).
/// </param>
/// <param name="CorrelationId">Groups a task/session.</param>
/// <param name="Outcome">Optional outcome summary.</param>
/// <param name="Error">Optional error message.</param>
/// <param name="DetailDocumentJson">
/// Optional agent-authored detail UI, as an A2UI adjacency-list document (see
/// <c>A2uiPresentationNormalizer</c>). When present, a presenter renders it in place of the built-in
/// payload detail — this is the "AI follows a JSON schema to show proper detail" seam. Kept separate
/// from <see cref="PayloadJson"/> so the raw domain payload and the presentation stay decoupled.
/// </param>
public sealed record ActivityEntry(
    string EntryId,
    ActivityEntryKind Kind,
    System.DateTimeOffset Timestamp,
    ActivityActor Actor,
    string Name,
    string? Category = null,
    string? PayloadJson = null,
    string? CausationId = null,
    string? CorrelationId = null,
    string? Outcome = null,
    string? Error = null,
    string? DetailDocumentJson = null);
