using System;
using System.Collections.Generic;

using FantaSim.App.World;   // WorldGlobeGeometry

namespace FantaSim.App.World.Composition;

/// <summary>
/// One scalar field's per-cell values. LIST form (not a FieldId-keyed dict) for
/// proxy/serializer friendliness.
/// </summary>
public sealed record WorldScalarFieldValues(FieldId Field, IReadOnlyList<double> Values);

/// <summary>
/// All resolved scalar fields at a tick. CellKeys = plate ids in geometry order (v1 = per-plate).
/// </summary>
public sealed record WorldFieldValues(
    long Tick,
    IReadOnlyList<string> CellKeys,
    IReadOnlyList<WorldScalarFieldValues> Scalars);

/// <summary>
/// The compute surface handed to each producer. Geometry + tick in; read upstream / write produced.
/// </summary>
public interface IFieldComputeContext
{
    long Tick { get; }
    WorldGlobeGeometry Geometry { get; }
    int CellCount { get; }
    IReadOnlyList<double> GetScalar(FieldId field);
    void SetScalar(FieldId field, IReadOnlyList<double> perCell);
}

// NOTE: IFieldHandoffComputeContext (body→sphere SphereHandoff) is DEFERRED — body formation is
// out of scope for Task 2. Port it when BodyFormation contracts are wired.

/// <summary>
/// Opt-in VALUE-compute capability (the value twin of LayerFieldBinding.Produces).
/// </summary>
public interface IFieldProducer : ILayer
{
    void Produce(IFieldComputeContext context);
}
