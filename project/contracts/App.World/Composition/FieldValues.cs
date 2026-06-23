using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

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

/// <summary>Plain 3-vector for app contracts. No Godot vector, ECS handle, or OpenUSD type.</summary>
public sealed record Vector3d(double X, double Y, double Z);

/// <summary>Mass fraction of one material component carried through formation and sphere handoff.</summary>
public sealed record MaterialCompositionFraction(string ComponentId, double Fraction);

/// <summary>
/// Body-formation to sphere-regime handoff. Carries the near-conserved formation quantities that
/// early sphere layers consume so magma-ocean state follows from formation products instead of
/// resetting to an unrelated visual default.
/// Sources: ref-projects/fantasim-app-godot/vault/architecture/body-formation-regime-graphs.md
///          ref-projects/fantasim-app-godot/project/contracts/App.World/BodyFormation.cs
/// </summary>
public sealed record SphereHandoff(
    long Tick,
    string SourceBodyId,
    double TotalMassKg,
    IReadOnlyList<MaterialCompositionFraction> BulkCompositionFractions,
    double RetainedHeatJ,
    double RetainedVolatileMassKg,
    Vector3d AngularMomentum,
    string LatentSubstrateSeed)
{
    public static SphereHandoff FromJson(JsonObject value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new SphereHandoff(
            Tick: Read<long>(value, "tick"),
            SourceBodyId: Read<string>(value, "sourceBodyId"),
            TotalMassKg: Read<double>(value, "totalMassKg"),
            BulkCompositionFractions: ReadCompositionFractions(value),
            RetainedHeatJ: Read<double>(value, "retainedHeatJ"),
            RetainedVolatileMassKg: Read<double>(value, "retainedVolatileMassKg"),
            AngularMomentum: ReadVector(value, "angularMomentum"),
            LatentSubstrateSeed: Read<string>(value, "latentSubstrateSeed"));
    }

    private static T Read<T>(JsonObject value, string key)
    {
        if (!value.TryGetPropertyValue(key, out var node) || node is null)
            throw new ArgumentException($"Sphere handoff JSON is missing '{key}'.", nameof(value));

        return node.GetValue<T>();
    }

    private static IReadOnlyList<MaterialCompositionFraction> ReadCompositionFractions(JsonObject value)
    {
        if (!value.TryGetPropertyValue("bulkCompositionFractions", out var node) || node is not JsonArray array)
            throw new ArgumentException("Sphere handoff JSON is missing 'bulkCompositionFractions'.", nameof(value));

        var result = new List<MaterialCompositionFraction>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonObject fraction)
                throw new ArgumentException("Sphere handoff composition entries must be JSON objects.", nameof(value));

            result.Add(new MaterialCompositionFraction(
                Read<string>(fraction, "componentId"),
                Read<double>(fraction, "fraction")));
        }

        return result;
    }

    private static Vector3d ReadVector(JsonObject value, string key)
    {
        if (!value.TryGetPropertyValue(key, out var node) || node is not JsonArray array || array.Count != 3)
            throw new ArgumentException($"Sphere handoff JSON '{key}' must contain three numbers.", nameof(value));

        return new Vector3d(
            array[0]!.GetValue<double>(),
            array[1]!.GetValue<double>(),
            array[2]!.GetValue<double>());
    }
}

/// <summary>
/// Optional extension of <see cref="IFieldComputeContext"/> for producers that need the latest
/// body-to-sphere handoff. Producers must still remain defined when the handoff is absent so older
/// field-resolution paths keep their current behavior.
/// </summary>
public interface IFieldHandoffComputeContext : IFieldComputeContext
{
    SphereHandoff? SphereHandoff { get; }
}

/// <summary>
/// Opt-in VALUE-compute capability (the value twin of LayerFieldBinding.Produces).
/// </summary>
public interface IFieldProducer : ILayer
{
    void Produce(IFieldComputeContext context);
}
