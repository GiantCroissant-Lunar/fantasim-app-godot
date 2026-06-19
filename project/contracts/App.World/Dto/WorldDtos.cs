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
