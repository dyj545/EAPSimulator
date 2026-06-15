using Newtonsoft.Json;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// Persistent layout data for one scenario's canvas.
/// </summary>
public class ScenarioLayout
{
    [JsonProperty("nodes")]
    public List<NodePosition> Nodes { get; set; } = [];

    [JsonProperty("connections")]
    public List<ConnectionDefinition> Connections { get; set; } = [];

    [JsonProperty("annotations")]
    public List<LineAnnotation> Annotations { get; set; } = [];

    [JsonProperty("viewState")]
    public CanvasViewState? ViewState { get; set; }
}

/// <summary>
/// Position of a single node (trigger or action) on the canvas.
/// </summary>
public class NodePosition
{
    [JsonProperty("nodeId")]
    public string NodeId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("stepIndex")]
    public int StepIndex { get; set; }

    /// <summary>
    /// "Trigger" or "Action".
    /// </summary>
    [JsonProperty("nodeType")]
    public string NodeType { get; set; } = "Trigger";

    [JsonProperty("x")]
    public double X { get; set; }

    [JsonProperty("y")]
    public double Y { get; set; }
}

/// <summary>
/// A directed connection between two nodes.
/// </summary>
public class ConnectionDefinition
{
    [JsonProperty("connectionId")]
    public string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("fromNodeId")]
    public string FromNodeId { get; set; } = "";

    [JsonProperty("toNodeId")]
    public string ToNodeId { get; set; } = "";

    [JsonProperty("label")]
    public string Label { get; set; } = "";

    [JsonProperty("labelX")]
    public double LabelX { get; set; }

    [JsonProperty("labelY")]
    public double LabelY { get; set; }

    /// <summary>
    /// "Sequential", "JudgementTrue", or "JudgementFalse".
    /// </summary>
    [JsonProperty("connectionType")]
    public string ConnectionType { get; set; } = "Sequential";
}

/// <summary>
/// Text annotation on a connection line.
/// </summary>
public class LineAnnotation
{
    [JsonProperty("connectionId")]
    public string ConnectionId { get; set; } = "";

    [JsonProperty("text")]
    public string Text { get; set; } = "";

    [JsonProperty("x")]
    public double X { get; set; }

    [JsonProperty("y")]
    public double Y { get; set; }
}

/// <summary>
/// Canvas viewport state (zoom and pan offset).
/// </summary>
public class CanvasViewState
{
    [JsonProperty("zoom")]
    public double Zoom { get; set; } = 1.0;

    [JsonProperty("offsetX")]
    public double OffsetX { get; set; }

    [JsonProperty("offsetY")]
    public double OffsetY { get; set; }
}
