using System;
using Xunit;

namespace App.Render.Tests;

public class MantleRequestTests
{
    [Fact]
    public void Parse_NullOrEmpty_ActivatesByDefault()
    {
        Assert.True(FantaSim.App.Render.MantleRequestParser.Parse(null).Enabled);
        Assert.True(FantaSim.App.Render.MantleRequestParser.Parse("").Enabled);
        Assert.True(FantaSim.App.Render.MantleRequestParser.Parse("   ").Enabled);
    }

    [Fact]
    public void Parse_EmptyObject_ActivatesByDefault()
    {
        Assert.True(FantaSim.App.Render.MantleRequestParser.Parse("{}").Enabled);
    }

    [Fact]
    public void Parse_EnabledTrue_Activates()
    {
        Assert.True(FantaSim.App.Render.MantleRequestParser.Parse("{\"enabled\":true}").Enabled);
    }

    [Fact]
    public void Parse_EnabledFalse_Deactivates()
    {
        Assert.False(FantaSim.App.Render.MantleRequestParser.Parse("{\"enabled\":false}").Enabled);
    }

    [Fact]
    public void Parse_NonBooleanEnabled_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.MantleRequestParser.Parse("{\"enabled\":\"yes\"}"));
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.MantleRequestParser.Parse("{\"enabled\":1}"));
    }

    [Fact]
    public void Parse_NotAnObject_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.MantleRequestParser.Parse("\"not an object\""));
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.MantleRequestParser.Parse("[1,2,3]"));
    }
}
