using System.Windows;
using System.Windows.Controls;
using EAPSimulator.Wpf.ViewModels;
using EAPSimulator.Wpf.ViewModels.FlowCanvas;

namespace EAPSimulator.Wpf.Views.FlowDesigner;

public partial class ScenarioFlowView : UserControl
{
    public ScenarioFlowView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private AutoReplyViewModel? ViewModel => DataContext as AutoReplyViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AutoReplyViewModel.SelectedScenario))
            {
                RebuildFlowDocument(vm);
            }
        };

        if (vm.SelectedScenario != null)
            RebuildFlowDocument(vm);
    }

    private void RebuildFlowDocument(AutoReplyViewModel parentVm)
    {
        var flowVm = parentVm.FlowCanvas;
        var scenario = parentVm.SelectedScenario;

        // Clean up existing nodes
        foreach (var node in flowVm.Document.Nodes)
            node.StepViewModel = null;

        flowVm.ClearSelection();
        flowVm.Document.Nodes.Clear();
        flowVm.Document.Connections.Clear();

        if (scenario == null)
        {
            flowVm.Document.ScenarioName = "";
            return;
        }

        flowVm.Document.ScenarioName = scenario.Name;

        const double laneWidth = 300;
        const double nodeWidth = 180;
        const double nodeHeight = 44;
        const double startY = 40;
        const double stepSpacing = 80;
        double y = startY;

        for (int i = 0; i < scenario.Steps.Count; i++)
        {
            var step = scenario.Steps[i];

            // Determine lanes
            var triggerLane = GetTriggerLane(step);
            var actionLane = GetActionLane(step);

            // Trigger node
            var triggerNode = new FlowNodeViewModel
            {
                NodeId = string.IsNullOrEmpty(step.NodeId)
                    ? Guid.NewGuid().ToString("N") : step.NodeId,
                StepIndex = i,
                NodeType = FlowNodeType.Trigger,
                Lane = triggerLane,
                Width = nodeWidth,
                Height = nodeHeight,
            };
            triggerNode.StepViewModel = step;

            double triggerLaneOffset = triggerLane switch
            {
                SwimLane.Equipment => 0,
                SwimLane.EAP => laneWidth,
                SwimLane.Host => laneWidth * 2,
                _ => laneWidth
            };
            triggerNode.X = triggerLaneOffset + (laneWidth - nodeWidth) / 2;
            triggerNode.Y = y;

            flowVm.Document.Nodes.Add(triggerNode);

            // Action node (if in different lane)
            if (actionLane != triggerLane)
            {
                var actionNode = new FlowNodeViewModel
                {
                    NodeId = Guid.NewGuid().ToString("N"),
                    StepIndex = i,
                    NodeType = FlowNodeType.Action,
                    Lane = actionLane,
                    Width = nodeWidth,
                    Height = nodeHeight,
                };
                actionNode.StepViewModel = step;

                double actionLaneOffset = actionLane switch
                {
                    SwimLane.Equipment => 0,
                    SwimLane.EAP => laneWidth,
                    SwimLane.Host => laneWidth * 2,
                    _ => laneWidth
                };
                actionNode.X = actionLaneOffset + (laneWidth - nodeWidth) / 2;
                actionNode.Y = y;

                flowVm.Document.Nodes.Add(actionNode);

                // Connect trigger → action
                flowVm.Document.Connections.Add(new FlowConnectionViewModel
                {
                    FromNodeId = triggerNode.NodeId,
                    ToNodeId = actionNode.NodeId,
                });
            }

            // Sequential connection to next step
            if (i < scenario.Steps.Count - 1)
            {
                // Find the "exit" node of current step
                var exitNodes = flowVm.Document.Nodes
                    .Where(n => n.StepIndex == i).ToList();
                var nextTriggerNodes = flowVm.Document.Nodes
                    .Where(n => n.StepIndex == i + 1 && n.NodeType == FlowNodeType.Trigger)
                    .ToList();

                if (exitNodes.Count > 0 && nextTriggerNodes.Count > 0)
                {
                    var exitNode = exitNodes.Count > 1
                        ? exitNodes.FirstOrDefault(n => n.NodeType == FlowNodeType.Action) ?? exitNodes[0]
                        : exitNodes[0];

                    flowVm.Document.Connections.Add(new FlowConnectionViewModel
                    {
                        FromNodeId = exitNode.NodeId,
                        ToNodeId = nextTriggerNodes[0].NodeId,
                    });
                }
            }

            y += nodeHeight + stepSpacing;
        }

        flowVm.InvalidateCanvas();
    }

    private static SwimLane GetTriggerLane(ScenarioStepViewModel step)
    {
        return step.TriggerType switch
        {
            ViewModels.TriggerType.Mapper => SwimLane.EAP,
            ViewModels.TriggerType.Judgement => SwimLane.EAP,
            ViewModels.TriggerType.SecsMessage =>
                step.HostInitiated ? SwimLane.EAP : SwimLane.Equipment,
            ViewModels.TriggerType.HostMessage => SwimLane.Host,
            ViewModels.TriggerType.EquipmentState => SwimLane.Equipment,
            _ => SwimLane.EAP
        };
    }

    private static SwimLane GetActionLane(ScenarioStepViewModel step)
    {
        return step.ActionType switch
        {
            ViewModels.ActionType.Mapper => SwimLane.EAP,
            ViewModels.ActionType.StateAlterer => SwimLane.EAP,
            ViewModels.ActionType.SecsReply =>
                step.HostInitiated ? SwimLane.Equipment : SwimLane.EAP,
            ViewModels.ActionType.HostMessage => SwimLane.Host,
            _ => SwimLane.EAP
        };
    }

    private void OnAddScenario(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddScenarioCommand.Execute(null);
    }

    private void OnDeleteScenario(object sender, RoutedEventArgs e)
    {
        ViewModel?.DeleteScenarioCommand.Execute(null);
    }
}
