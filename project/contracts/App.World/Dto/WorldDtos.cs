namespace FantaSim.App.World.Dto;

public record WorldOverview(
    string WorldId,
    string Name,
    int EntityCount,
    int FieldCount,
    bool IsDirty
);

public record WorldFieldValues(
    IReadOnlyDictionary<string, object> FieldValues
);

public record WorldScalarFieldValues(
    IReadOnlyDictionary<string, float> ScalarValues
);

public record WorldRenderSnapshot(
    long FrameIndex,
    IReadOnlyList<RenderEntityDto> Entities
);

public record RenderEntityDto(
    string EntityId,
    float PositionX,
    float PositionY,
    float PositionZ,
    float RotationX,
    float RotationY,
    float RotationZ,
    string MeshId
);

public record WorldFieldValuesRequest(
    string WorldId,
    IReadOnlyList<string> FieldIds
);

public record WorldScalarFieldValuesRequest(
    string WorldId,
    IReadOnlyList<string> ScalarFieldIds
);

public record WorldGenerationRequest(
    string WorldId,
    string GenerationSpec,
    Dictionary<string, object> Parameters
);

public record WorldGenerationResult(
    bool Success,
    string Message,
    string ResultWorldId
);

public record WorldGenerationChangedEvent(
    string WorldId,
    string ChangeType,
    object Detail
);

// ---- Globe reconstruction (Phase 0 heartbeat) ----
// Plain-float, Godot-free snapshot of a seeded plate globe at its base (tick-0) geometry. The T4
// seam builds an ArrayMesh from the cell corners and rotates them on the GPU per plate (axis +
// rate-per-tick) from a tick uniform, so motion is shader-driven (no per-tick CPU rebuild).

public readonly record struct GlobeVec3(float X, float Y, float Z);

public record GlobeCell(
    int CellId,
    int PlateId,
    GlobeVec3 C0,
    GlobeVec3 C1,
    GlobeVec3 C2
);

public record GlobePlate(
    int PlateId,
    GlobeVec3 Axis,
    double RatePerTick
);

public record WorldGlobeSnapshot(
    int Frequency,
    int CellCount,
    int PlateCount,
    IReadOnlyList<GlobeCell> Cells,
    IReadOnlyList<GlobePlate> Plates
);
