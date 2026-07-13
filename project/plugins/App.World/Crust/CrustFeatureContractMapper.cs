using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Crust;

namespace FantaSim.App.World.Crust;

/// <summary>
/// Explicit package boundary between engine-owned crust semantics and app-owned presentation
/// contracts. Engine enum ordinals are deliberately never cast into the wire contract.
/// </summary>
internal static class CrustFeatureContractMapper
{
    internal static TectonicFeatureKind ToContractKind(CrustFeatureKind kind) => kind switch
    {
        CrustFeatureKind.None => TectonicFeatureKind.None,
        CrustFeatureKind.Mountain => TectonicFeatureKind.Mountain,
        CrustFeatureKind.VolcanicArc => TectonicFeatureKind.VolcanicArc,
        CrustFeatureKind.Trench => TectonicFeatureKind.Trench,
        CrustFeatureKind.Ridge => TectonicFeatureKind.Ridge,
        CrustFeatureKind.Fault => TectonicFeatureKind.Fault,
        _ => TectonicFeatureKind.None,
    };

    internal static CellCrustFeature ToCellFeature(CrustFeature feature)
    {
        var contractKind = ToContractKind(feature.Kind);
        return new CellCrustFeature(contractKind.ToWireByte(), feature.Magnitude);
    }

    internal static CrustFeatureKind ToEngineKind(byte wireKind)
        => TectonicFeatureKindExtensions.FromWireByte(wireKind) switch
        {
            TectonicFeatureKind.Mountain => CrustFeatureKind.Mountain,
            TectonicFeatureKind.VolcanicArc => CrustFeatureKind.VolcanicArc,
            TectonicFeatureKind.Trench => CrustFeatureKind.Trench,
            TectonicFeatureKind.Ridge => CrustFeatureKind.Ridge,
            TectonicFeatureKind.Fault => CrustFeatureKind.Fault,
            _ => CrustFeatureKind.None,
        };
}
