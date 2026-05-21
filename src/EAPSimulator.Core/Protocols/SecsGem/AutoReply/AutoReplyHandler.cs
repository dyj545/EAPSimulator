using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Handlers;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// ISecsMessageHandler for quick-reply rules.
/// Supports conditional matching: checks FieldConditions before replying.
/// If conditions don't match, returns null (falls through to other handlers).
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
        foreach (var cond in conditions)
        {
            if (!MatchesCondition(cond, rootItem))
                return false;
        }
        return true;
    }

    private static bool MatchesCondition(FieldCondition condition, SecsItem? rootItem)
    {
        if (rootItem == null || string.IsNullOrEmpty(condition.Path))
            return string.IsNullOrEmpty(condition.Value);

        var item = NavigatePath(rootItem, condition.Path);
        if (item == null) return false;

        var itemValue = GetItemValueString(item);
        return ScenarioEngine.EvaluateCondition(itemValue, condition.Operator, condition.Value);
    }

    internal static SecsItem? NavigatePath(SecsItem root, string path)
    {
        var parts = path.Split('/');
        SecsItem? current = root;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var index))
                return null;

            if (current is SecsList list)
            {
                if (index < 0 || index >= list.Items.Length)
                    return null;
                current = list.Items[index];
            }
            else
            {
                if (index != 0) return null;
            }
        }

        return current;
    }

    internal static string GetItemValueString(SecsItem item) => item switch
    {
        SecsAscii a => a.Value,
        SecsBinary b => string.Join(" ", b.Value.Select(bt => $"{bt:X2}")),
        SecsBoolean bo => bo.Value ? "1" : "0",
        SecsU1 u1 => u1.Value.Length == 1 ? u1.Value[0].ToString() : $"[{string.Join(",", u1.Value)}]",
        SecsU2 u2 => u2.Value.Length == 1 ? u2.Value[0].ToString() : $"[{string.Join(",", u2.Value)}]",
        SecsU4 u4 => u4.Value.Length == 1 ? u4.Value[0].ToString() : $"[{string.Join(",", u4.Value)}]",
        SecsU8 u8 => u8.Value.Length == 1 ? u8.Value[0].ToString() : $"[{string.Join(",", u8.Value)}]",
        SecsI1 i1 => i1.Value.Length == 1 ? i1.Value[0].ToString() : $"[{string.Join(",", i1.Value)}]",
        SecsI2 i2 => i2.Value.Length == 1 ? i2.Value[0].ToString() : $"[{string.Join(",", i2.Value)}]",
        SecsI4 i4 => i4.Value.Length == 1 ? i4.Value[0].ToString() : $"[{string.Join(",", i4.Value)}]",
        SecsI8 i8 => i8.Value.Length == 1 ? i8.Value[0].ToString() : $"[{string.Join(",", i8.Value)}]",
        SecsF4 f4 => f4.Value.Length == 1 ? f4.Value[0].ToString() : $"[{string.Join(",", f4.Value)}]",
        SecsF8 f8 => f8.Value.Length == 1 ? f8.Value[0].ToString() : $"[{string.Join(",", f8.Value)}]",
        _ => item.ToString() ?? ""
    };
}
