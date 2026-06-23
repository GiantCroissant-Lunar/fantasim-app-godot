// Declares this contract assembly as shared across the host-bundle ALC boundary, holding only
// interfaces/DTOs (no [Plugin] types) to preserve type identity between host and collectible
// contexts. Mirrors App.Camera/AssemblyInfo.cs.
[assembly: PluginArchi.Extensibility.Abstractions.PluginSharedContract]
