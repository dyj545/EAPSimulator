using System.Collections.ObjectModel;

namespace EAPSimulator.Wpf.ViewModels.FlowCanvas;

public partial class FlowDocument
{
    public ObservableCollection<FlowNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<FlowConnectionViewModel> Connections { get; } = [];
    public string ScenarioName { get; set; } = "";

    public FlowNodeViewModel? FindNode(string nodeId)
    {
        return Nodes.FirstOrDefault(n => n.NodeId == nodeId);
    }

    public void AutoLayout(double startX, double startY)
    {
        var stepGroups = Nodes
            .Where(n => n.NodeType == FlowNodeType.Trigger)
            .GroupBy(n => n.StepIndex)
            .OrderBy(g => g.Key);

        double y = startY;
        const double nodeSpacing = 80;
        const double nodeHeight = 44;
        const double nodeWidth = 180;
        const double laneWidth = 300;

        foreach (var group in stepGroups)
        {
            foreach (var node in group)
            {
                var laneOffset = node.Lane switch
                {
                    SwimLane.Equipment => 0,
                    SwimLane.EAP => laneWidth,
                    SwimLane.Host => laneWidth * 2,
                    _ => laneWidth
                };
                node.X = startX + laneOffset + (laneWidth - nodeWidth) / 2;
                node.Y = y;
                node.Width = nodeWidth;
                node.Height = nodeHeight;
            }
            y += nodeHeight + nodeSpacing;
        }
    }
}
