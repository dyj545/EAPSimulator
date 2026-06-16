using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Shared helpers for matching <see cref="FieldCondition"/>s against <see cref="SecsItem"/> trees.
/// Used by both QuickReply (<see cref="AutoReplyHandler"/>) and the scenario engine.
/// </summary>
internal static class MatchUtil
{
    public static bool MatchesCondition(FieldCondition condition, SecsItem? rootItem)
    {
        if (rootItem == null || string.IsNullOrEmpty(condition.Path))
            return string.IsNullOrEmpty(condition.Value);

        var item = NavigatePath(rootItem, condition.Path);
        if (item == null) return false;

        var itemValue = GetItemValueString(item);
        return EvaluateCondition(itemValue, condition.Operator, condition.Value);
    }

    public static bool EvaluateCondition(string itemValue, string op, string expected) => op switch
    {
        "==" => string.Equals(itemValue, expected, StringComparison.OrdinalIgnoreCase),
        "!=" => !string.Equals(itemValue, expected, StringComparison.OrdinalIgnoreCase),
        "contains" => itemValue.Contains(expected, StringComparison.OrdinalIgnoreCase),
        ">" or "<" or ">=" or "<=" =>
            double.TryParse(itemValue, out var a) && double.TryParse(expected, out var b)
                ? EvaluateNumeric(a, op, b)
                : string.Compare(itemValue, expected, StringComparison.OrdinalIgnoreCase) switch
                {
                    < 0 => op is "<" or "<=",
                    0 => op is ">=" or "<=",
                    > 0 => op is ">" or ">=",
                },
        _ => string.Equals(itemValue, expected, StringComparison.OrdinalIgnoreCase),
    };

    private static bool EvaluateNumeric(double a, string op, double b) => op switch
    {
        ">" => a > b,
        "<" => a < b,
        ">=" => a >= b,
        "<=" => a <= b,
        _ => false,
    };

    /// <summary>
    /// Navigate a SECS item tree by "0/1/2" path. For non-list root, "0" returns the root.
    /// </summary>
    public static SecsItem? NavigatePath(SecsItem root, string path)
    {
        var parts = path.Split('/');
        SecsItem? current = root;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var index))
                return null;

            if (current is SecsList list)
            {
                if (index < 0 || index >= list.Items.Length) return null;
                current = list.Items[index];
            }
            else
            {
                if (index != 0) return null;
            }
        }

        return current;
    }

    public static string GetItemValueString(SecsItem item) => item switch
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
