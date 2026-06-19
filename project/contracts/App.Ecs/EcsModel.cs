namespace FantaSim.App.Ecs;

public enum EcsBackendKind { Arch = 0 }

public enum EcsWorldChangeKind { Created = 0, Initialized = 1, Destroyed = 2 }

public sealed record EcsWorldSpec(
    string WorldId,
    EcsBackendKind Backend = EcsBackendKind.Arch,
    string? DisplayName = null,
    int InitialEntityCapacity = 1024,
    bool DebugMode = false);

public sealed record EcsWorldInfo(
    string WorldId,
    EcsBackendKind Backend,
    string DisplayName,
    int EntityCount,
    int RegisteredSystemCount,
    bool Initialized);

public sealed record EcsWorldChangedMessage(
    string WorldId,
    EcsWorldChangeKind ChangeKind,
    EcsWorldInfo? World,
    DateTimeOffset Timestamp);
