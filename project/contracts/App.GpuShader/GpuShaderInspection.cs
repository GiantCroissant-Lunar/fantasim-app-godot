namespace FantaSim.App.GpuShader;

/// <summary>
/// Result of loading a (non-compute) Godot shader resource through the resident seam.
/// ShaderKind values mirror Godot shader modes: "spatial", "canvas_item", "particles",
/// "sky", "fog". The Godot Shader resource itself never crosses the seam.
/// </summary>
public sealed record GpuShaderInspection(
    string ResourcePath,
    bool Found,
    string? ShaderKind = null,
    int SourceLength = 0,
    string? Error = null);
