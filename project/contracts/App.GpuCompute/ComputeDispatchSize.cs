namespace FantaSim.App.GpuCompute;

/// <summary>Compute workgroup dispatch counts.</summary>
public sealed record ComputeDispatchSize(uint X, uint Y, uint Z = 1);
