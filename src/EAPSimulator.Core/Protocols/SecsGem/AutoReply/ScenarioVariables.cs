using System.Text.RegularExpressions;
using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Variable bag for a scenario run. Holds string-valued variables and provides
/// <c>${name}</c>-style interpolation for SECS / Host message templates.
///
/// Variables are scoped to a single run of <see cref="ScenarioEngine"/> and shared
/// across nested loops and called sub-scenarios — there is no local scope.
/// Names with dots (e.g. <c>$loop.L1.i</c>) are valid; the engine writes Loop
/// iteration counters under that namespace.
/// </summary>
public sealed class ScenarioVariables
{
    // \$\{ ... \}  — captures everything up to the next "}". Names may contain
    // letters, digits, underscores, dots and a leading $ (for $loop.* counters).
    private static readonly Regex VarPattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    private readonly Dictionary<string, string> _vars = new(StringComparer.Ordinal);

    public void Set(string name, string? value) => _vars[name] = value ?? "";

    public string? Get(string name) => _vars.TryGetValue(name, out var v) ? v : null;

    public bool TryGet(string name, out string value)
    {
        if (_vars.TryGetValue(name, out var v)) { value = v; return true; }
        value = "";
        return false;
    }

    public IReadOnlyDictionary<string, string> Snapshot() => _vars;

    public void Clear() => _vars.Clear();

    /// <summary>
    /// Replace every <c>${name}</c> with the corresponding variable value.
    /// Unknown names are left as-is — that way templates remain debuggable when a
    /// variable is missing rather than silently disappearing.
    /// </summary>
    public string Render(string? template)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        if (template.IndexOf("${", StringComparison.Ordinal) < 0) return template; // fast path
        return VarPattern.Replace(template, m =>
        {
            var name = m.Groups[1].Value.Trim();
            return _vars.TryGetValue(name, out var v) ? v : m.Value;
        });
    }

    /// <summary>
    /// Render variables across a built <see cref="HostMessage"/> in place — leaf
    /// field values and the <see cref="HostMessage.RawBody"/> template.
    /// </summary>
    public void RenderInPlace(HostMessage msg)
    {
        if (msg == null) return;
        if (!string.IsNullOrEmpty(msg.RawBody))
            msg.RawBody = Render(msg.RawBody);
        foreach (var kv in msg.Fields)
            RenderField(kv.Value);
    }

    private void RenderField(HostField f)
    {
        if (!string.IsNullOrEmpty(f.Value))
            f.Value = Render(f.Value);
        foreach (var c in f.Children)
            RenderField(c);
    }

    /// <summary>
    /// Read a SECS item at "0/1/2" path and return its string representation,
    /// or null if the path doesn't exist. Wraps <see cref="MatchUtil.NavigatePath"/>
    /// + <see cref="MatchUtil.GetItemValueString"/> so callers don't depend on both.
    /// </summary>
    public static string? ReadSecsPath(SecsItem? root, string path)
    {
        if (root == null) return null;
        var item = string.IsNullOrEmpty(path) ? root : MatchUtil.NavigatePath(root, path);
        return item == null ? null : MatchUtil.GetItemValueString(item);
    }

    /// <summary>
    /// Read a flat Host field by name. Empty name returns the message <see cref="HostMessage.Name"/>
    /// (mirrors the convention <see cref="MatchUtil.MatchesCondition(FieldCondition, HostMessage?)"/> uses).
    /// </summary>
    public static string? ReadHostField(HostMessage? msg, string fieldName)
    {
        if (msg == null) return null;
        if (string.IsNullOrEmpty(fieldName)) return msg.Name;
        return msg.GetFieldValue(fieldName);
    }
}
