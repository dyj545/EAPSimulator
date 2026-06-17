using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EAPSimulator.UI.ViewModels;

namespace EAPSimulator.UI.Views;

public partial class FieldEditDialog : Window
{
    private readonly SecsItemViewModel _item = null!;
    private readonly ObservableCollection<ValueMappingEntry> _mappings = null!;

    // ─── 原始值快照（用于修改检测） ───
    private readonly string _origTypeName = "";
    private readonly string _origAlias = "";
    private readonly string _origDescription = "";
    private readonly string _origValueText = "";
    private readonly string _origFormat = "";
    private readonly string _origInputFormat = "";
    private readonly string _origPreviewFormat = "";
    private readonly string _origNlb = "";
    private readonly string _origDefaultValue = "";
    private readonly List<ValueMappingEntry> _origMappings = [];

    public bool IsConfirmed { get; private set; }

    public FieldEditDialog()
    {
        InitializeComponent();
    }

    public FieldEditDialog(SecsItemViewModel item) : this()
    {
        _item = item;
        _mappings = new ObservableCollection<ValueMappingEntry>(
            item.ValueMappings.Select(m => m.Clone()));

        // Load current values
        TypeNameCombo.SelectedItem = item.TypeName;
        AliasBox.Text = item.Alias;
        DescriptionBox.Text = item.Description;
        ValueText.Text = item.ValueText;
        FormatBox.Text = item.Format;
        InputFormatCombo.SelectedItem = NormalizeOption(item.InputFormat, SecsItemViewModel.InputFormatOptions, "Dec");
        PreviewFormatCombo.SelectedItem = NormalizeOption(item.PreviewFormat, SecsItemViewModel.PreviewFormatOptions, "Dec");
        NlbCombo.SelectedItem = NormalizeOption(item.Nlb, SecsItemViewModel.NlbOptions, "Auto");
        DefaultValueBox.Text = item.DefaultValue;

        MappingList.ItemsSource = _mappings;
        UpdateMappingEmptyState();
        UpdatePreview();

        // 保存原始值快照
        _origTypeName = item.TypeName;
        _origAlias = item.Alias;
        _origDescription = item.Description;
        _origValueText = item.ValueText;
        _origFormat = item.Format;
        _origInputFormat = (string)InputFormatCombo.SelectedItem!;
        _origPreviewFormat = (string)PreviewFormatCombo.SelectedItem!;
        _origNlb = (string)NlbCombo.SelectedItem!;
        _origDefaultValue = item.DefaultValue;
        _origMappings = item.ValueMappings.Select(m => m.Clone()).ToList();
    }

    private static string NormalizeOption(string current, string[] options, string fallback) =>
        options.Contains(current) ? current : fallback;

    private bool HasChanges()
    {
        if (TypeNameCombo.SelectedItem as string != _origTypeName) return true;
        if ((AliasBox.Text ?? "") != _origAlias) return true;
        if ((DescriptionBox.Text ?? "") != _origDescription) return true;
        if ((ValueText.Text ?? "") != _origValueText) return true;
        if ((FormatBox.Text ?? "") != _origFormat) return true;
        if ((InputFormatCombo.SelectedItem as string ?? "") != _origInputFormat) return true;
        if ((PreviewFormatCombo.SelectedItem as string ?? "") != _origPreviewFormat) return true;
        if ((NlbCombo.SelectedItem as string ?? "") != _origNlb) return true;
        if ((DefaultValueBox.Text ?? "") != _origDefaultValue) return true;
        if (_mappings.Count != _origMappings.Count) return true;
        for (int i = 0; i < _mappings.Count; i++)
        {
            if (_mappings[i].Value != _origMappings[i].Value ||
                _mappings[i].DisplayText != _origMappings[i].DisplayText)
                return true;
        }
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (HasChanges())
                _ = PromptSaveAndClose();
            else
                Close(false);
            return;
        }
        base.OnKeyDown(e);
    }

    private async Task PromptSaveAndClose()
    {
        var result = await ShowSavePrompt();
        if (result == true)
            OnOkClick(null!, new RoutedEventArgs());
        else if (result == false)
            Close(false);
    }

    private async Task<bool?> ShowSavePrompt()
    {
        var tcs = new TaskCompletionSource<bool?>();

        var dialog = new Window
        {
            Title = "保存修改",
            Width = 340, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new DockPanel
            {
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Children =
                        {
                            new Button
                            {
                                Content = "保存", Width = 72, Padding = new Avalonia.Thickness(4),
                                Classes = { "accent" },
                            },
                            new Button
                            {
                                Content = "不保存", Width = 72, Padding = new Avalonia.Thickness(4),
                            },
                            new Button
                            {
                                Content = "取消", Width = 72, Padding = new Avalonia.Thickness(4),
                            }
                        }
                    },
                    new TextBlock
                    {
                        Text = "检测到修改，是否保存？",
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    }
                }
            }
        };

        var buttons = ((StackPanel)((DockPanel)dialog.Content!).Children[0]).Children;
        ((Button)buttons[0]).Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        ((Button)buttons[1]).Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        ((Button)buttons[2]).Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; tcs.TrySetResult(null); dialog.Close(); }
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(null);

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        // Apply changes to the item
        if (TypeNameCombo.SelectedItem is string typeName)
            _item.TypeName = typeName;
        _item.Alias = AliasBox.Text ?? string.Empty;
        _item.Description = DescriptionBox.Text ?? string.Empty;
        _item.ValueText = ValueText.Text ?? string.Empty;
        _item.Format = FormatBox.Text ?? string.Empty;
        _item.InputFormat = InputFormatCombo.SelectedItem as string ?? "Dec";
        _item.PreviewFormat = PreviewFormatCombo.SelectedItem as string ?? "Dec";
        _item.Nlb = NlbCombo.SelectedItem as string ?? "Auto";
        _item.DefaultValue = DefaultValueBox.Text ?? string.Empty;

        // Apply value mappings
        _item.ValueMappings.Clear();
        foreach (var mapping in _mappings)
        {
            if (!string.IsNullOrWhiteSpace(mapping.Value) || !string.IsNullOrWhiteSpace(mapping.DisplayText))
                _item.ValueMappings.Add(mapping);
        }

        IsConfirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnAddMapping(object? sender, RoutedEventArgs e)
    {
        _mappings.Add(new ValueMappingEntry());
        UpdateMappingEmptyState();
    }

    private void OnRemoveMapping(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ValueMappingEntry entry)
        {
            _mappings.Remove(entry);
            UpdateMappingEmptyState();
        }
    }

    private void UpdateMappingEmptyState()
    {
        MappingEmptyText.IsVisible = _mappings.Count == 0;
    }

    private void UpdatePreview()
    {
        var alias = AliasBox.Text ?? string.Empty;
        var value = ValueText.Text ?? string.Empty;
        var typeName = TypeNameCombo.SelectedItem as string ?? _item.TypeName;
        var isList = typeName == "L";

        if (!string.IsNullOrEmpty(alias))
        {
            PreviewDisplayText.Text = isList ? alias : $"{alias}: {value}";
        }
        else
        {
            PreviewDisplayText.Text = isList ? $"L[{_item.Children.Count}]" : $"{typeName} {value}";
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(alias)) parts.Add($"Name: {alias}");
        var desc = DescriptionBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(desc)) parts.Add($"Desc: {desc}");
        var format = FormatBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(format)) parts.Add($"Format: {format}");
        var inFmt = InputFormatCombo.SelectedItem as string ?? "";
        if (!string.IsNullOrEmpty(inFmt) && inFmt != "Dec") parts.Add($"Input: {inFmt}");
        var prevFmt = PreviewFormatCombo.SelectedItem as string ?? "";
        if (!string.IsNullOrEmpty(prevFmt) && prevFmt != "Dec") parts.Add($"Preview: {prevFmt}");
        var nlb = NlbCombo.SelectedItem as string ?? "";
        if (!string.IsNullOrEmpty(nlb) && nlb != "Auto") parts.Add($"NLB: {nlb}");
        var defVal = DefaultValueBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(defVal)) parts.Add($"Default: {defVal}");
        if (_mappings.Count > 0) parts.Add($"Mappings: {_mappings.Count}");

        PreviewToolTip.Text = parts.Count > 0 ? string.Join("\n", parts) : "(无额外信息)";
    }
}
