using EAPSimulator.Core.Protocols;
using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EAPSimulator.Core.Tests;

/// <summary>
/// Direct tests for <see cref="ScenarioExpression"/> — exercise the sandboxed identifier set
/// (vars/secs/host/loop) plus the helper functions (num/contains/startsWith/endsWith) without
/// going through the engine. The engine integration is covered by <see cref="ScenarioEngineTests"/>.
/// </summary>
public class ScenarioExpressionTests
{
    [Fact]
    public void EmptyExpression_TreatedAsTrue()
    {
        var expr = new ScenarioExpression(new ScenarioVariables());
        Assert.True(expr.EvaluateBool(null));
        Assert.True(expr.EvaluateBool(""));
        Assert.True(expr.EvaluateBool("   "));
    }

    [Fact]
    public void VarsIndexer_ReadsValuesAndReturnsEmptyForMissing()
    {
        var vars = new ScenarioVariables();
        vars.Set("lot", "LOT001");
        var expr = new ScenarioExpression(vars);

        Assert.True(expr.EvaluateBool("vars[\"lot\"] == \"LOT001\""));
        Assert.False(expr.EvaluateBool("vars[\"lot\"] == \"LOT002\""));
        // Missing variable resolves to "" — comparison against "" passes for unset vars.
        Assert.True(expr.EvaluateBool("vars[\"missing\"] == \"\""));
    }

    [Fact]
    public void SecsIndexer_ReadsPathFromLastReceived()
    {
        var expr = new ScenarioExpression(new ScenarioVariables());
        // Build a list root with two children — root[0] = "ALPHA", root[1] = "BETA"
        var root = new SecsList(new SecsItem[]
        {
            new SecsAscii("ALPHA"),
            new SecsAscii("BETA"),
        });
        expr.UpdateLastSecs(root);

        Assert.True(expr.EvaluateBool("secs[\"0\"] == \"ALPHA\""));
        Assert.True(expr.EvaluateBool("secs[\"1\"] == \"BETA\""));
        // Out-of-range path yields "" — comparisons against "" work; arithmetic is a separate concern.
        Assert.True(expr.EvaluateBool("secs[\"99\"] == \"\""));
    }

    [Fact]
    public void NumHelper_ParsesAndCompares()
    {
        var vars = new ScenarioVariables();
        vars.Set("count", "42");
        var expr = new ScenarioExpression(vars);

        Assert.True(expr.EvaluateBool("num(vars[\"count\"]) > 10"));
        Assert.True(expr.EvaluateBool("num(vars[\"count\"]) >= 42"));
        Assert.False(expr.EvaluateBool("num(vars[\"count\"]) < 0"));
        // Non-numeric → 0; guarantees the expression never throws on dirty input.
        Assert.True(expr.EvaluateBool("num(\"abc\") == 0"));
    }

    [Fact]
    public void ContainsHelper_IsCaseInsensitive()
    {
        var vars = new ScenarioVariables();
        vars.Set("lot", "Lot-Wafer-001");
        var expr = new ScenarioExpression(vars);

        Assert.True(expr.EvaluateBool("contains(vars[\"lot\"], \"wafer\")"));
        Assert.True(expr.EvaluateBool("startsWith(vars[\"lot\"], \"LOT\")"));
        Assert.True(expr.EvaluateBool("endsWith(vars[\"lot\"], \"001\")"));
    }

    [Fact]
    public void HostAccessor_ExposesFieldsAndMessageName()
    {
        var expr = new ScenarioExpression(new ScenarioVariables());
        var msg = new HostMessage { Name = "MAP_COUNT_REQ" };
        // HostMessage.SetFieldValue only mutates pre-existing fields, so populate Fields directly.
        msg.Fields["LotID"] = new HostField { Name = "LotID", Value = "LOT123" };
        msg.Fields["WaferCount"] = new HostField { Name = "WaferCount", Value = "25" };
        expr.UpdateLastHost(msg);

        Assert.True(expr.EvaluateBool("host.Name == \"MAP_COUNT_REQ\""));
        Assert.True(expr.EvaluateBool("host[\"LotID\"] == \"LOT123\""));
        Assert.True(expr.EvaluateBool("num(host[\"WaferCount\"]) == 25"));
    }

    [Fact]
    public void LoopAccessor_ReflectsCurrentIteration()
    {
        var expr = new ScenarioExpression(new ScenarioVariables());
        expr.SetLoopIteration("L1", 3);
        Assert.True(expr.EvaluateBool("num(loop[\"L1\"]) == 3"));
        expr.ClearLoopIteration("L1");
        Assert.True(expr.EvaluateBool("num(loop[\"L1\"]) == 0"));
    }

    [Fact]
    public void SyntaxError_ReturnsFalseWithDiagnostic()
    {
        var expr = new ScenarioExpression(new ScenarioVariables());
        var result = expr.EvaluateBool("this is not a valid expression", out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void LogicalCombinators_Work()
    {
        var vars = new ScenarioVariables();
        vars.Set("a", "1");
        vars.Set("b", "2");
        var expr = new ScenarioExpression(vars);

        Assert.True(expr.EvaluateBool("num(vars[\"a\"]) > 0 && num(vars[\"b\"]) > 0"));
        Assert.False(expr.EvaluateBool("num(vars[\"a\"]) < 0 || num(vars[\"b\"]) < 0"));
    }
}
