namespace FantaSim.App.GpuCompute;

/// <summary>
/// Identifies an imported Godot RDShaderFile resource by stable id and resource path.
/// The shader file itself is PCK/resource content, not an ALC-loaded assembly.
/// </summary>
public sealed record ComputeShaderReference(
    string ShaderId,
    string ShaderPath,
    string Version = "");
