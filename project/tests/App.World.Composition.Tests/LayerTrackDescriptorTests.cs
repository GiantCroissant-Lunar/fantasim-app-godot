using System.Text.Json;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.World.Composition.Tests;

/// <summary>
/// Round-trip + forward-compat coverage for the layer-track registry's wire contract
/// (vault/specs/2026-07-10-layer-track-registry-design.md). These types cross the T3/T4
/// seam and, eventually, a Unity consumer -- so an unknown field on read must never throw,
/// and every canonical-tick field must survive a JSON round-trip byte-for-byte.
/// </summary>
public sealed class LayerTrackDescriptorTests
{
    private static LayerTrackDescriptor SampleDescriptor() => new(
        SphereId: "geosphere",
        LayerId: "geosphere.crust",
        StreamId: new LayerTrackStreamId(
            Variation: "main",
            Branch: "default",
            L: "L2",
            Domain: "world",
            Model: "default"),
        DisplayName: "Crust",
        State: LayerTrackStates.Declared,
        TimeDomain: new LayerTrackTimeDomain(
            StartTick: 100_000_000L,
            EndTick: null,
            Rung: "ka"),
        Content: new LayerTrackContent(
            Type: LayerTrackContentTypes.Filmstrip,
            Source: "geosphere.crust.layer",
            CadenceTicks: 5_000_000L),
        Capabilities: new[] { "scrub", "toggle", "expand-graph" },
        SourceRef: "geosphere.crust.layer");

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var original = SampleDescriptor();

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<LayerTrackDescriptor>(json);

        Assert.NotNull(restored);
        AssertDescriptorsEqual(original, restored!);
    }

    [Fact]
    public void RoundTrip_PreservesOpenEndedTimeDomain()
    {
        var original = SampleDescriptor();
        Assert.Null(original.TimeDomain.EndTick);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<LayerTrackDescriptor>(json);

        Assert.Null(restored!.TimeDomain.EndTick);
    }

    [Fact]
    public void Deserialize_ToleratesUnknownExtraFields()
    {
        // Forward-compat: a future (e.g. Unity) writer may add fields this app does not know
        // about yet. Reading must never throw -- the round-trip degradation guarantee.
        const string json = """
        {
          "sphereId": "geosphere",
          "layerId": "geosphere.crust",
          "streamId": { "variation": "main", "branch": "default", "l": "L2", "domain": "world", "model": "default", "future": "value" },
          "displayName": "Crust",
          "state": "declared",
          "timeDomain": { "startTick": 100000000, "endTick": null, "rung": "ka" },
          "content": { "type": "filmstrip", "source": "geosphere.crust.layer", "cadenceTicks": 5000000 },
          "capabilities": ["scrub", "toggle"],
          "sourceRef": "geosphere.crust.layer",
          "unknownTopLevelField": { "nested": true }
        }
        """;

        var restored = JsonSerializer.Deserialize<LayerTrackDescriptor>(json);

        Assert.NotNull(restored);
        Assert.Equal("geosphere.crust", restored!.LayerId);
    }

    [Theory]
    [InlineData("declared")]
    [InlineData("discovered")]
    [InlineData("archived")]
    [InlineData("some-future-state-a-newer-writer-invented")]
    public void State_AcceptsUnknownStringsWithoutThrowing(string state)
    {
        var descriptor = SampleDescriptor() with { State = state };

        var json = JsonSerializer.Serialize(descriptor);
        var restored = JsonSerializer.Deserialize<LayerTrackDescriptor>(json);

        Assert.Equal(state, restored!.State);
    }

    [Fact]
    public void RegistrySnapshot_RoundTrips()
    {
        var snapshot = new LayerTrackRegistrySnapshot(
            Revision: 3,
            Tracks: new[] { SampleDescriptor() });

        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<LayerTrackRegistrySnapshot>(json);

        Assert.NotNull(restored);
        Assert.Equal(3, restored!.Revision);
        Assert.Single(restored.Tracks);
        AssertDescriptorsEqual(SampleDescriptor(), restored.Tracks[0]);
    }

    // Record-generated equality compares IReadOnlyList<string> members by reference (the
    // interface has no value-equality contract), so a deserialized List<string> never equals the
    // originally-authored array via plain Assert.Equal(record, record). Compare field-by-field
    // instead, using xUnit's own sequence-equality overload for the collection field.
    private static void AssertDescriptorsEqual(LayerTrackDescriptor expected, LayerTrackDescriptor actual)
    {
        Assert.Equal(expected.SphereId, actual.SphereId);
        Assert.Equal(expected.LayerId, actual.LayerId);
        Assert.Equal(expected.StreamId, actual.StreamId);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.TimeDomain, actual.TimeDomain);
        Assert.Equal(expected.Content, actual.Content);
        Assert.Equal(expected.Capabilities, actual.Capabilities);
        Assert.Equal(expected.SourceRef, actual.SourceRef);
    }
}
