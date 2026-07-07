using Xunit;

namespace App.Render.Tests;

public class ExplodedRequestTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsAssembled()
    {
        var r = FantaSim.App.Render.ExplodedRequestParser.Parse(null);
        Assert.True(r.IsAssembled);
        Assert.Equal(0.0, r.Factor);

        r = FantaSim.App.Render.ExplodedRequestParser.Parse("");
        Assert.True(r.IsAssembled);
        Assert.Equal(0.0, r.Factor);
    }

    [Fact]
    public void Parse_EmptyObject_ReturnsAssembled()
    {
        var r = FantaSim.App.Render.ExplodedRequestParser.Parse("{}");
        Assert.True(r.IsAssembled);
        Assert.Equal(0.0, r.Factor);
    }

    [Fact]
    public void Parse_FactorZero_ReturnsAssembled()
    {
        var r = FantaSim.App.Render.ExplodedRequestParser.Parse("{\"factor\":0}");
        Assert.True(r.IsAssembled);
        Assert.Equal(0.0, r.Factor);
    }

    [Fact]
    public void Parse_ValidPayload_ReturnsValue()
    {
        var r = FantaSim.App.Render.ExplodedRequestParser.Parse("{\"factor\":0.3}");
        Assert.False(r.IsAssembled);
        Assert.Equal(0.3, r.Factor);
    }

    [Fact]
    public void Parse_FactorOne_ReturnsOne()
    {
        var r = FantaSim.App.Render.ExplodedRequestParser.Parse("{\"factor\":1}");
        Assert.False(r.IsAssembled);
        Assert.Equal(1.0, r.Factor);
    }

    [Fact]
    public void Parse_NegativeFactor_ClampedToZero()
    {
        var r = FantaSim.App.Render.ExplodedRequestParser.Parse("{\"factor\":-0.5}");
        Assert.True(r.IsAssembled);
        Assert.Equal(0.0, r.Factor);
    }

    [Fact]
    public void Parse_FactorOverOne_ClampedToOne()
    {
        var r = FantaSim.App.Render.ExplodedRequestParser.Parse("{\"factor\":2.5}");
        Assert.False(r.IsAssembled);
        Assert.Equal(1.0, r.Factor);
    }

    [Fact]
    public void Parse_NotAnObject_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.ExplodedRequestParser.Parse("\"not an object\""));
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.ExplodedRequestParser.Parse("[1,2,3]"));
    }

    [Fact]
    public void Parse_NonNumericFactor_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.ExplodedRequestParser.Parse("{\"factor\":\"half\"}"));
    }

    [Fact]
    public void Parse_IntegerFactor_AcceptedAsDouble()
    {
        var r = FantaSim.App.Render.ExplodedRequestParser.Parse("{\"factor\":1}");
        Assert.Equal(1.0, r.Factor);
    }
}
