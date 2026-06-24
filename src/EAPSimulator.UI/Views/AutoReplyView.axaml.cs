using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace EAPSimulator.UI.Views;

public partial class AutoReplyView : UserControl
{
    private ViewModels.AutoReplyViewModel? ViewModel => DataContext as ViewModels.AutoReplyViewModel;

    public AutoReplyView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SetupNullVisibility();
    }

    private void SetupNullVisibility()
    {
        var vm = ViewModel;
        if (vm == null) return;

        vm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(vm.SelectedQuickReply):
                    var hasQuickReply = vm.SelectedQuickReply != null;
                    if (QuickReplyDetailContent != null)
                        QuickReplyDetailContent.IsVisible = hasQuickReply;
                    if (QuickReplyEmptyState != null)
                        QuickReplyEmptyState.IsVisible = !hasQuickReply;
                    break;

                case nameof(vm.SelectedScenario):
                    if (ScenarioSettingsBorder != null)
                        ScenarioSettingsBorder.IsVisible = vm.SelectedScenario != null;
                    break;

                case nameof(vm.SelectedStep):
                    var hasStep = vm.SelectedStep != null;
                    if (StepDetailBorder != null)
                        StepDetailBorder.IsVisible = hasStep;
                    if (StepEmptyState != null)
                        StepEmptyState.IsVisible = !hasStep;
                    break;
            }
        };

        // Initialize visibility
        if (QuickReplyDetailContent != null)
            QuickReplyDetailContent.IsVisible = vm.SelectedQuickReply != null;
        if (QuickReplyEmptyState != null)
            QuickReplyEmptyState.IsVisible = vm.SelectedQuickReply == null;
        if (ScenarioSettingsBorder != null)
            ScenarioSettingsBorder.IsVisible = vm.SelectedScenario != null;
        if (StepDetailBorder != null)
            StepDetailBorder.IsVisible = vm.SelectedStep != null;
        if (StepEmptyState != null)
            StepEmptyState.IsVisible = vm.SelectedStep == null;
    }

    private void SetupActionTemplateFiltering()
    {
        // Removed: the new scenario UI uses simple template ComboBoxes per step kind, no fuzzy search.
    }

    private void OnQuickReplyTabClick(object? sender, RoutedEventArgs e)
    {
        QuickReplyPanel.IsVisible = true;
        ScenarioPanel.IsVisible = false;
    }

    private void OnScenarioTabClick(object? sender, RoutedEventArgs e)
    {
        QuickReplyPanel.IsVisible = false;
        ScenarioPanel.IsVisible = true;
    }

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择自动回复规则文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON Files") { Patterns = ["*.json"] }, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && ViewModel != null)
        {
            var path = files[0].Path.LocalPath;
            ViewModel.LoadFromPath(path);
            ViewModel.StatusMessage = $"已加载 {System.IO.Path.GetFileName(path)}";
        }
    }

    private void OnActionTemplateDropDownOpened(object? sender, EventArgs e)
    {
        // Stub kept for any leftover bindings; no-op.
    }

    private void OnActionTemplateDropDownClosed(object? sender, EventArgs e)
    {
        // No action needed on close
    }

    private void OnReplyTemplateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && e.AddedItems.Count > 0 && e.AddedItems[0] is string selected)
        {
            if (cb.DataContext is ViewModels.QuickReplyRuleViewModel rule)
                rule.ReplyTemplateName = selected;
        }
    }

    /// <summary>
    /// ƒx toggle on a condition row: flips between legacy (Path/Operator/Value) and expression mode.
    /// Going expression → legacy clears Expression; legacy → expression seeds Expression from the
    /// current legacy fields so the user sees what the engine would have synthesized.
    /// </summary>
    private void OnToggleExpressionMode(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not ViewModels.FieldConditionViewModel cond) return;

        if (cond.IsExpressionMode)
        {
            // Drop expression — legacy fields are kept verbatim.
            cond.Expression = "";
        }
        else
        {
            cond.Expression = SynthesizeExpression(cond);
        }
    }

    /// <summary>
    /// Translate a legacy Path/Operator/Value condition into the equivalent expression string
    /// so users can switch modes and edit further. Mirrors <see cref="EAPSimulator.Core.Protocols.SecsGem.AutoReply.MatchUtil.EvaluateCondition"/>
    /// for the supported operators.
    /// </summary>
    private static string SynthesizeExpression(ViewModels.FieldConditionViewModel cond)
    {
        var path = cond.Path ?? "";
        var op = cond.Operator ?? "==";
        var val = cond.Value ?? "";
        var lhs = string.IsNullOrEmpty(path) ? "\"\"" : $"secs[\"{path}\"]";
        if (op == "contains")
            return $"contains({lhs}, \"{val.Replace("\"", "\\\"")}\")";
        if (op is ">" or "<" or ">=" or "<=")
            return $"num({lhs}) {op} num(\"{val.Replace("\"", "\\\"")}\")";
        // == / != — string comparison, matches MatchUtil's OrdinalIgnoreCase semantics best-effort.
        return $"{lhs} {op} \"{val.Replace("\"", "\\\"")}\"";
    }

}
