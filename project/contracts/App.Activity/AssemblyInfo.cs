// Declares this contract assembly as shared across the host<->bundle ALC boundary. It holds
// interfaces/DTOs only (no [Plugin] types) - the activity entry records and the ledger service
// contract - so their type identity is preserved between the host and the collectible contexts.
[assembly: PluginArchi.Extensibility.Abstractions.PluginSharedContract]
