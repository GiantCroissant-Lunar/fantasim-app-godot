using System;
using FantaSim.App.World;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationGraphFamilyLayerSelectionTests
{
    [Fact]
    public void TrySelectLayer_ExistingBinding_SwitchesActiveGraph()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var switched = source.TrySelectLayer(
            WorldGenerationGraphDefaults.GeosphereSphereId,
            "geosphere.crust",
            tick: 0,
            regimeId: "mobile-plate");

        Assert.True(switched);
        Assert.Equal(WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId, source.ActiveGraphId);
    }

    [Fact]
    public void TrySelectLayer_RegimeSpecificBinding_PrefersRegimeSpecificGraph()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var switched = source.TrySelectLayer(
            WorldGenerationGraphDefaults.GeosphereSphereId,
            "geosphere.magma-ocean",
            tick: 0,
            regimeId: "magma-ocean");

        Assert.True(switched);
        Assert.Equal(WorldGenerationGraphDefaults.GeosphereMagmaOceanGraphId, source.ActiveGraphId);
    }

    [Fact]
    public void TrySelectLayer_MissingBinding_ReturnsFalse_AndLeavesGraphUnchanged()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var previousGraphId = source.ActiveGraphId;
        var switched = source.TrySelectLayer(
            WorldGenerationGraphDefaults.GeosphereSphereId,
            "geosphere.nonexistent",
            tick: 0,
            regimeId: "mobile-plate");

        Assert.False(switched);
        Assert.Equal(previousGraphId, source.ActiveGraphId);
    }

    [Fact]
    public void SelectLayer_ExistingBinding_SwitchesActiveGraph()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        source.SelectLayer(
            WorldGenerationGraphDefaults.GeosphereSphereId,
            "geosphere.plate",
            tick: 0,
            regimeId: "mobile-plate");

        Assert.Equal(WorldGenerationGraphDefaults.GeospherePlateLayerGraphId, source.ActiveGraphId);
    }

    [Fact]
    public void SelectLayer_MissingBinding_Throws()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        Assert.Throws<ArgumentException>(() => source.SelectLayer(
            WorldGenerationGraphDefaults.GeosphereSphereId,
            "geosphere.nonexistent",
            tick: 0,
            regimeId: "mobile-plate"));
    }
}
