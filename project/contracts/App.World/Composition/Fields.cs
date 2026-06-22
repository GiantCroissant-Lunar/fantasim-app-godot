namespace FantaSim.App.World.Composition;

/// <summary>
/// Unique identity of a field in the world layer stack
/// (e.g. "geosphere.elevation", "atmosphere.precipitation").
/// </summary>
public readonly record struct FieldId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Unique identity of a layer in the world layer stack
/// (e.g. "geosphere.plate", "geosphere.crust").
/// </summary>
public readonly record struct LayerId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// The domain a field lives in: per-cell (Voronoi cell id),
/// per-globe (scalar/aggregate), or per-feature.
/// </summary>
public enum FieldDomain
{
    Cell,
    Globe,
    Feature
}

/// <summary>
/// The value kind a field carries: scalar, vector, categorical, or mask.
/// </summary>
public enum FieldValueKind
{
    Scalar,
    Vector,
    Categorical,
    Mask
}

/// <summary>
/// Static declaration of a field's identity, domain, and value kind.
/// </summary>
public sealed record FieldDescriptor(FieldId Id, FieldDomain Domain, FieldValueKind ValueKind);

/// <summary>
/// One field a layer consumes. <paramref name="Required"/> = true means absence (no producer in
/// the stack) is a composition error; = false means optional. <paramref name="Default"/> is an
/// optional fallback that SATISFIES an optional consumption even when no layer produces the field
/// (USD attribute-fallback). The typed default lands with the field value-type system; object? is
/// the skeleton placeholder.
/// </summary>
public sealed record FieldConsumption(FieldId Field, bool Required = true, object? Default = null);

/// <summary>
/// A layer's declared field contract: what it produces and what it consumes.
/// </summary>
public sealed record LayerFieldBinding(
    LayerId Layer,
    IReadOnlyList<FieldId> Produces,
    IReadOnlyList<FieldConsumption> Consumes);

/// <summary>
/// The kind of composition error detected when resolving a layer stack's field graph.
/// </summary>
public enum FieldCompositionErrorKind
{
    /// <summary>
    /// A produced/consumed FieldId with no FieldDescriptor declared.
    /// </summary>
    UnknownField,

    /// <summary>
    /// A Required consumption with no producer in the stack.
    /// </summary>
    UnresolvedRequiredField,

    /// <summary>
    /// A producer->consumer dependency cycle exists.
    /// </summary>
    Cycle,

    /// <summary>
    /// The same LayerId appears more than once in the stack (ambiguous producer resolution).
    /// </summary>
    DuplicateLayer
}

/// <summary>
/// One error found during field composition.
/// </summary>
public sealed record FieldCompositionError(FieldCompositionErrorKind Kind, string Message);

/// <summary>
/// Result of composing a layer stack's field graph.
/// </summary>
public sealed record FieldCompositionResult(
    IReadOnlyList<LayerId> ExecutionOrder,
    IReadOnlyDictionary<FieldId, LayerId> WinningProducers,
    IReadOnlyList<FieldId> UnsatisfiedOptionalFields,
    IReadOnlyList<FieldCompositionError> Errors)
{
    /// <summary>
    /// True when no composition errors were detected.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}
