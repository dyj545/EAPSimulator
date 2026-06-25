using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;

namespace EAPSimulator.UI.Views;

public partial class AutoReplyView : UserControl
{
    private ViewModels.AutoReplyViewModel? ViewModel => DataContext as ViewModels.AutoReplyViewModel;
    private readonly ConditionalWeakTable<AutoCompleteBox, object> _typedBoxes = new();
    private static readonly object TypedMarker = new();

    public AutoReplyView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        AddHandler(AutoCompleteBox.PointerPressedEvent, OnAutoCompleteBoxPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(AutoCompleteBox.TextInputEvent, OnAutoCompleteBoxTextInput, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(GotFocusEvent, OnTextInputGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnTextInputPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
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

    private static bool IsTemplateAutoCompleteBox(AutoCompleteBox box)
    {
        return box.Classes.Contains("templatePicker");
    }

    private static AutoCompleteBox? FindTemplateAutoCompleteBox(object? source)
    {
        return source switch
        {
            AutoCompleteBox box when IsTemplateAutoCompleteBox(box) => box,
            TextBox tb => tb.FindAncestorOfType<AutoCompleteBox>() is { } box && IsTemplateAutoCompleteBox(box) ? box : null,
            Control control => control.FindAncestorOfType<AutoCompleteBox>() is { } box && IsTemplateAutoCompleteBox(box) ? box : null,
            _ => null,
        };
    }

    private void OpenTemplateDropDown(AutoCompleteBox box)
    {
        if (!IsTemplateAutoCompleteBox(box) || !box.IsEffectivelyEnabled) return;
        // Let AutoCompleteBox process its own Text/SearchText update first, then force the popup
        // open. This is only called from user pointer/key-driven paths; do not call from plain
        // GotFocus because selection changes can focus a template box while the user clicked elsewhere.
        Dispatcher.UIThread.Post(() =>
        {
            if (box.IsEffectivelyEnabled && _typedBoxes.TryGetValue(box, out _))
                box.IsDropDownOpen = true;
        }, DispatcherPriority.Input);
    }

    private static void SelectAllTemplateTextBox(TextBox textBox)
    {
        if (FindTemplateAutoCompleteBox(textBox) is not { }) return;

        // AutoCompleteBox may update the inner TextBox during focus routing; dispatch once so
        // selection wins after the template's own handlers have synchronized Text. Do NOT call
        // Focus() here: when the user clicks another step, a delayed Focus() would bring focus
        // back to the old template box and its binding update could re-open a popup.
        Dispatcher.UIThread.Post(() =>
        {
            if (textBox.IsKeyboardFocusWithin)
                textBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnAutoCompleteBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FindTemplateAutoCompleteBox(e.Source) is not { } box) return;
        _typedBoxes.Remove(box);
        box.IsDropDownOpen = false;
        if (e.Source is TextBox tb)
            SelectAllTemplateTextBox(tb);
    }

    private void OnAutoCompleteBoxTextInput(object? sender, TextInputEventArgs e)
    {
        if (FindTemplateAutoCompleteBox(e.Source) is not { } box) return;
        _typedBoxes.Remove(box);
        _typedBoxes.Add(box, TypedMarker);
        OpenTemplateDropDown(box);
    }

    private void OnTextInputGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is TextBox tb)
            SelectAllTemplateTextBox(tb);
    }

    private void OnTextInputPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not TextBox tb) return;
        SelectAllTemplateTextBox(tb);
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
        // 已弃用：回复模板下拉已改为 AutoCompleteBox + Text 双向绑定，VM 直接通过 setter 同步。
        // 保留空方法以便已构建的 XAML 引用不报错——下一次清理时连同未引用的 axaml.cs 一起删除。
    }

    ///<summary>
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
            cond.Expression = SynthesizeExpression(cond, IsInsideHostReceiveEditor(btn));
        }
    }

    private static bool IsInsideHostReceiveEditor(Control control)
    {
        return control.GetVisualAncestors()
            .OfType<StackPanel>()
            .Any(panel => Equals(panel.Tag, "HostReceiveConditions"));
    }

    /// <summary>
    /// Translate a legacy Path/Operator/Value condition into the equivalent expression string
    /// so users can switch modes and edit further. Mirrors <see cref="EAPSimulator.Core.Protocols.SecsGem.AutoReply.MatchUtil.EvaluateCondition"/>
    /// for the supported operators.
    /// </summary>
    private static string SynthesizeExpression(ViewModels.FieldConditionViewModel cond, bool hostContext = false)
    {
        var path = cond.Path ?? "";
        var op = cond.Operator ?? "==";
        var val = cond.Value ?? "";
        var lhs = string.IsNullOrEmpty(path)
            ? (hostContext ? "host.Name" : "\"\"")
            : hostContext ? $"host[\"{path}\"]" : $"secs[\"{path}\"]";
        if (op == "contains")
            return $"contains({lhs}, \"{val.Replace("\"", "\\\"")}\")";
        if (op is ">" or "<" or ">=" or "<=")
            return $"num({lhs}) {op} num(\"{val.Replace("\"", "\\\"")}\")";
        // == / != — string comparison, matches MatchUtil's OrdinalIgnoreCase semantics best-effort.
        return $"{lhs} {op} \"{val.Replace("\"", "\\\"")}\"";
    }

    private void OnToggleListViewClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.IsFlowView = false;
    }

    private void OnToggleFlowViewClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.IsFlowView = true;
    }

    private void OnResetLayoutClick(object? sender, RoutedEventArgs e)
    {
        FlowCanvas?.ResetLayout();
    }

}
