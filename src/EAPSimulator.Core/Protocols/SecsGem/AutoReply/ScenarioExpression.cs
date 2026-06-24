using DynamicExpresso;
using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Sandboxed expression engine that powers Branch / Receive-conditions / LoopWhile.
/// Wraps <see cref="DynamicExpresso.Interpreter"/> with a fixed identifier set so user-written
/// expressions can only read the scenario context — no <c>System.IO</c>, no reflection.
///
/// Available identifiers in every expression:
/// <list type="bullet">
///   <item><c>vars["name"]</c>  — value of a scenario variable as string ("" if missing)</item>
///   <item><c>secs["0/1/2"]</c> — string value of a SECS item at the path on the last received message</item>
///   <item><c>host["fieldName"]</c> — host field value on the last received host message ("" if absent)</item>
///   <item><c>host.name</c>     — last host message's <see cref="HostMessage.Name"/></item>
///   <item><c>loop["LoopId"]</c> — current 1-based iteration of a named loop (0 if not active)</item>
///   <item><c>num(x)</c>        — parse x as double (returns 0 if not numeric)</item>
///   <item><c>contains(s, t)</c>, <c>startsWith(s, t)</c>, <c>endsWith(s, t)</c> — case-insensitive helpers</item>
/// </list>
///
/// <para>Design choice: indexer-style access (<c>secs["0/1/2"]</c>) rather than property access
/// (<c>secs.0.1.2</c>) — the latter is not valid C# and would require a custom DLR-style binder.
/// String paths also match the existing JSON schema for <see cref="FieldCondition.Path"/>.</para>
/// </summary>
public sealed class ScenarioExpression
{
    /// <summary>
    /// Indexer wrapper around a single source object — used to expose <c>secs["path"]</c>,
    /// <c>host["field"]</c>, <c>vars["name"]</c>, <c>loop["id"]</c> in expressions.
    /// </summary>
    public sealed class Accessor
    {
        private readonly Func<string, string> _read;
        internal Accessor(Func<string, string> read) => _read = read;
        public string this[string key] => _read(key ?? "") ?? "";
    }

    /// <summary>
    /// Same as <see cref="Accessor"/> but also exposes a top-level <c>name</c> property for
    /// <c>host.name</c> — DynamicExpresso resolves member access against the runtime type.
    /// </summary>
    public sealed class HostAccessor
    {
        private readonly Func<string, string> _read;
        internal HostAccessor(Func<string, string> read, string name) { _read = read; Name = name; }
        public string this[string key] => _read(key ?? "") ?? "";
        public string Name { get; }
    }

    private readonly Interpreter _interpreter;
    private readonly ScenarioVariables _vars;
    private SecsItem? _lastSecs;
    private HostMessage? _lastHost;
    private readonly Dictionary<string, int> _loopIters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _foreachItems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _foreachIndex = new(StringComparer.Ordinal);

    public ScenarioExpression(ScenarioVariables vars)
    {
        _vars = vars;
        // PrivateAccess off + only our identifiers reachable = no System.IO / Activator surface.
        _interpreter = new Interpreter(InterpreterOptions.Default);
        _interpreter.SetVariable("vars", new Accessor(name => _vars.Get(name) ?? ""));
        _interpreter.SetVariable("secs", new Accessor(path => ScenarioVariables.ReadSecsPath(_lastSecs, path) ?? ""));
        _interpreter.SetVariable("loop", new Accessor(id =>
            _loopIters.TryGetValue(id ?? "", out var i) ? i.ToString() : "0"));
        _interpreter.SetVariable("foreach", new Accessor(id =>
            _foreachItems.TryGetValue(id ?? "", out var item) ? item : ""));
        _interpreter.SetVariable("foreachIndex", new Accessor(id =>
            _foreachIndex.TryGetValue(id ?? "", out var i) ? i.ToString() : "-1"));
        // Host accessor is rebound on every UpdateHost call — it carries the message name.
        RefreshHostBinding();

        // Helper functions — wrapped lambdas so the user doesn't need to remember
        // double.Parse / string.Contains overloads. All string ops are case-insensitive.
        _interpreter.SetFunction("num", (Func<string, double>)(s =>
            double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0));
        _interpreter.SetFunction("contains", (Func<string, string, bool>)((a, b) =>
            (a ?? "").Contains(b ?? "", StringComparison.OrdinalIgnoreCase)));
        _interpreter.SetFunction("startsWith", (Func<string, string, bool>)((a, b) =>
            (a ?? "").StartsWith(b ?? "", StringComparison.OrdinalIgnoreCase)));
        _interpreter.SetFunction("endsWith", (Func<string, string, bool>)((a, b) =>
            (a ?? "").EndsWith(b ?? "", StringComparison.OrdinalIgnoreCase)));
    }

    public void UpdateLastSecs(SecsItem? root) => _lastSecs = root;

    public void UpdateLastHost(HostMessage? msg)
    {
        _lastHost = msg;
        RefreshHostBinding();
    }

    public void SetLoopIteration(string loopId, int iteration)
    {
        if (string.IsNullOrEmpty(loopId)) return;
        _loopIters[loopId] = iteration;
    }

    public void ClearLoopIteration(string loopId)
    {
        if (string.IsNullOrEmpty(loopId)) return;
        _loopIters.Remove(loopId);
    }

    public void SetForEachItem(string id, string item, int index)
    {
        if (string.IsNullOrEmpty(id)) return;
        _foreachItems[id] = item ?? "";
        _foreachIndex[id] = index;
    }

    public void ClearForEach(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _foreachItems.Remove(id);
        _foreachIndex.Remove(id);
    }

    private void RefreshHostBinding()
    {
        var name = _lastHost?.Name ?? "";
        var host = new HostAccessor(
            field => ScenarioVariables.ReadHostField(_lastHost, field) ?? "",
            name);
        _interpreter.SetVariable("host", host);
    }

    /// <summary>
    /// Evaluate <paramref name="expression"/> as a boolean.
    /// Empty / whitespace expression → <c>true</c> (so an unconfigured guard never blocks).
    /// Returns <c>false</c> and stashes the error message into <paramref name="error"/> on parse failure.
    /// </summary>
    public bool EvaluateBool(string? expression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression)) return true;
        try
        {
            var result = _interpreter.Eval(expression);
            return result switch
            {
                bool b => b,
                null => false,
                string s => !string.IsNullOrEmpty(s) && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase),
                int i => i != 0,
                double d => d != 0,
                _ => true,
            };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Convenience overload that swallows the error and returns false on parse failure.</summary>
    public bool EvaluateBool(string? expression) => EvaluateBool(expression, out _);
}
