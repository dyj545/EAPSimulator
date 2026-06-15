using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Handlers;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Manages scenario execution: tracks active scenario state, matches triggers,
/// and produces reply messages.
/// </summary>
public class ScenarioEngine : ISecsMessageHandler
{
    private readonly ILogger _logger;
    private readonly List<ScenarioDefinition> _scenarios;
    private readonly Func<string, SecsMessageTemplate?> _templateLookup;
    private readonly Action<string, string>? _stateAlterHandler;

    // Track which scenario is active and which step we're on
    private ScenarioDefinition? _activeScenario;
    private int _currentStepIndex;

    /// <summary>Raised when a scenario triggers a Host message action.</summary>
    public event EventHandler<HostActionEventArgs>? HostActionTriggered;

    /// <summary>Raised when a scenario triggers a state alteration.</summary>
    public event EventHandler<StateAlterEventArgs>? StateAlterTriggered;

    public ScenarioEngine(
        ILogger logger,
        IEnumerable<ScenarioDefinition> scenarios,
        Func<string, SecsMessageTemplate?> templateLookup,
        Action<string, string>? stateAlterHandler = null)
    {
        _logger = logger;
        _scenarios = scenarios.Where(s => s.Enabled).ToList();
        _templateLookup = templateLookup;
        _stateAlterHandler = stateAlterHandler;
    }

    /// <summary>
    /// Invoke the state alter handler and raise the StateAlterTriggered event.
    /// </summary>
    public void AlterState(string variableName, string newValue)
    {
        _stateAlterHandler?.Invoke(variableName, newValue);
        StateAlterTriggered?.Invoke(this, new StateAlterEventArgs(variableName, newValue));
    }

    /// <summary>
    /// Raise the HostActionTriggered event.
    /// </summary>
    public void TriggerHostAction(string hostMessageName)
    {
        HostActionTriggered?.Invoke(this, new HostActionEventArgs(hostMessageName));
    }

    /// <summary>
    /// Handle a Host/MES message, forwarding it through scenario matching.
    /// Currently a stub — host message scenario matching is not yet implemented.
    /// </summary>
    public void HandleHostMessage(string messageName, Dictionary<string, string> fields)
    {
        _logger.LogDebug("Host message '{Name}' received with {Count} fields", messageName, fields.Count);
        // TODO: Implement host message scenario matching
    }

    /// <summary>
    /// Try to match an incoming message against any scenario.
    /// Returns a reply message if matched, null otherwise.
    /// </summary>
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        // If a scenario is active, try to match the current step
        if (_activeScenario != null)
        {
            var step = _activeScenario.Steps[_currentStepIndex];
            if (MatchesStep(step, request))
            {
                _logger.LogInformation("Scenario '{Name}' step {Step} matched: S{S}F{F}",
                    _activeScenario.Name, _currentStepIndex, request.Stream, request.Function);
                return AdvanceScenario(request);
            }
        }

        // Try to start a new scenario from step 0
        foreach (var scenario in _scenarios)
        {
            if (scenario.Steps.Count == 0) continue;
            var firstStep = scenario.Steps[0];
            if (MatchesStep(firstStep, request))
            {
                _activeScenario = scenario;
                _currentStepIndex = 0;
                _logger.LogInformation("Scenario '{Name}' started at step 0: S{S}F{F}",
                    scenario.Name, request.Stream, request.Function);
                return AdvanceScenario(request);
            }
        }

        return Task.FromResult<SecsMessage?>(null);
    }

    private Task<SecsMessage?> AdvanceScenario(SecsMessage request)
    {
        var step = _activeScenario!.Steps[_currentStepIndex];
        SecsMessage? reply = null;

        // Build reply from the step's action template
        if (!string.IsNullOrEmpty(step.ActionTemplateName))
        {
            var template = _templateLookup(step.ActionTemplateName);
            if (template != null)
            {
                reply = template.BuildMessage();
                reply.SystemBytes = request.SystemBytes;
                reply.WBit = false;
            }
            else
            {
                _logger.LogWarning("Scenario '{Name}' step {Step}: template '{Template}' not found",
                    _activeScenario.Name, _currentStepIndex, step.ActionTemplateName);
            }
        }

        // Advance to next step
        _currentStepIndex++;
        if (_currentStepIndex >= _activeScenario.Steps.Count)
        {
            if (_activeScenario.Loop)
            {
                _currentStepIndex = 0;
                _logger.LogInformation("Scenario '{Name}' looping back to step 0", _activeScenario.Name);
            }
            else
            {
                _logger.LogInformation("Scenario '{Name}' completed", _activeScenario.Name);
                _activeScenario = null;
                _currentStepIndex = 0;
            }
        }

        return Task.FromResult(reply);
    }

    private bool MatchesStep(ScenarioStep step, SecsMessage msg)
    {
        if (step.Stream != 0 && step.Stream != msg.Stream) return false;
        if (step.Function != 0 && step.Function != msg.Function) return false;

        // Check field conditions
        foreach (var cond in step.Conditions)
        {
            if (!MatchesCondition(cond, msg.RootItem))
                return false;
        }

        return true;
    }

    private bool MatchesCondition(FieldCondition condition, SecsItem? rootItem)
    {
        if (rootItem == null || string.IsNullOrEmpty(condition.Path))
            return string.IsNullOrEmpty(condition.Value);

        var item = NavigatePath(rootItem, condition.Path);
        if (item == null) return false;

        var itemValue = GetItemValueString(item);
        return EvaluateCondition(itemValue, condition.Operator, condition.Value);
    }

    internal static bool EvaluateCondition(string itemValue, string op, string expected)
    {
        return op switch
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
    }

    private static bool EvaluateNumeric(double a, string op, double b) => op switch
    {
        ">" => a > b,
        "<" => a < b,
        ">=" => a >= b,
        "<=" => a <= b,
        _ => false,
    };

    /// <summary>
    /// Navigate a SECS item tree by path. "0/1/2" means root (if list) -> item[0] -> item[1] -> item[2].
    /// For non-list root, "0" means the root itself.
    /// </summary>
    private static SecsItem? NavigatePath(SecsItem root, string path)
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
                // Non-list item: index 0 means "this item"
                if (index != 0) return null;
                // current stays the same
            }
        }

        return current;
    }

    private static string GetItemValueString(SecsItem item) => item switch
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
