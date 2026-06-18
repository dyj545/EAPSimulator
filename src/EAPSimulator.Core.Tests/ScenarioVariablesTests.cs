using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Xunit;

namespace EAPSimulator.Core.Tests;

public class ScenarioVariablesTests
{
    [Fact]
    public void Render_ReplacesKnownVarsLeavesUnknown()
    {
        var vars = new ScenarioVariables();
        vars.Set("lot", "L42");
        Assert.Equal("L42_x", vars.Render("${lot}_x"));
        Assert.Equal("${unknown}", vars.Render("${unknown}"));
        Assert.Equal("a-L42-${unknown}-b", vars.Render("a-${lot}-${unknown}-b"));
    }

    [Fact]
    public void Render_NoBraceTextIsFastPath()
    {
        var vars = new ScenarioVariables();
        Assert.Equal("plain text", vars.Render("plain text"));
        Assert.Equal("", vars.Render(""));
        Assert.Equal("", vars.Render(null));
    }

    [Fact]
    public void Render_DotNamesWork()
    {
        var vars = new ScenarioVariables();
        vars.Set("$loop.L1.i", "3");
        Assert.Equal("iter=3", vars.Render("iter=${$loop.L1.i}"));
    }

    [Fact]
    public void RenderInPlace_RendersHostFieldValuesAndRawBody()
    {
        var vars = new ScenarioVariables();
        vars.Set("lot", "L77");
        var msg = new HostMessage
        {
            Name = "DEMO",
            RawBody = "lot=${lot}",
        };
        msg.Fields["lot"] = new HostField { Name = "lot", Value = "${lot}" };
        msg.Fields["nested"] = new HostField
        {
            Name = "nested",
            Children = { new HostField { Name = "child", Value = "x-${lot}" } },
        };

        vars.RenderInPlace(msg);
        Assert.Equal("lot=L77", msg.RawBody);
        Assert.Equal("L77", msg.Fields["lot"].Value);
        Assert.Equal("x-L77", msg.Fields["nested"].Children[0].Value);
    }

    [Fact]
    public void ReadSecsPath_ReturnsLeafString()
    {
        var root = SecsItem.L(SecsItem.A("hello"), SecsItem.U2(42));
        Assert.Equal("hello", ScenarioVariables.ReadSecsPath(root, "0"));
        Assert.Equal("42", ScenarioVariables.ReadSecsPath(root, "1"));
        Assert.Null(ScenarioVariables.ReadSecsPath(root, "9"));
    }

    [Fact]
    public void ReadHostField_FallsBackToName()
    {
        var msg = new HostMessage { Name = "ACK" };
        msg.Fields["code"] = new HostField { Name = "code", Value = "0" };
        Assert.Equal("ACK", ScenarioVariables.ReadHostField(msg, ""));
        Assert.Equal("0", ScenarioVariables.ReadHostField(msg, "code"));
    }
}
