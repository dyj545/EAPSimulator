using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Handlers;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// ISecsMessageHandler for quick-reply rules.
/// Supports conditional matching: checks FieldConditions before replying.
/// If conditions don't match, returns null (falls through to other handlers).
///
/// QuickReply has no scenario-style variable bag, but expression-mode conditions are still
/// honoured by allocating a throwaway <see cref="ScenarioExpression"/> per evaluation —
/// users get the same <c>secs["0/1/2"]</c> syntax across QuickReply and Scenario.
/// </summary>
public class AutoReplyHandler : ISecsMessageHandler
{
    private readonly List<(List<FieldCondition> Conditions, SecsMessage Reply)> _rules = new();

    public void AddRule(List<FieldCondition> conditions, SecsMessageTemplate replyTemplate)
    {
        var reply = replyTemplate.BuildMessage();
        reply.WBit = false;
        _rules.Add((conditions, reply));
    }

    public int RuleCount => _rules.Count;

    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        foreach (var (conditions, reply) in _rules)
        {
            if (MatchesConditions(conditions, request.RootItem))
            {
                var matched = CloneReply(reply);
                matched.SystemBytes = request.SystemBytes;
                return Task.FromResult<SecsMessage?>(matched);
            }
        }

        // No conditions matched — fall through
        return Task.FromResult<SecsMessage?>(null);
    }

    private static SecsMessage CloneReply(SecsMessage reply)
    {
        return new SecsMessage(reply.Stream, reply.Function, false, reply.RootItem);
    }

    private static bool MatchesConditions(List<FieldCondition> conditions, SecsItem? rootItem)
    {
        // Lazily allocate the evaluator only when an expression-mode condition is encountered —
        // pure-legacy rule sets pay zero cost.
        ScenarioExpression? evaluator = null;
        foreach (var cond in conditions)
        {
            if (cond.IsExpressionMode && evaluator == null)
                evaluator = new ScenarioExpression(new ScenarioVariables());
            if (!MatchUtil.MatchesCondition(cond, rootItem, evaluator))
                return false;
        }
        return true;
    }
}
