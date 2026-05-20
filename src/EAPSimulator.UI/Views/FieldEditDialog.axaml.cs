using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EAPSimulator.UI.ViewModels;

namespace EAPSimulator.UI.Views;

public partial class FieldEditDialog : Window
{
    private readonly SecsItemViewModel _item = null!;
    private readonly ObservableCollection<ValueMappingEntry> _mappings = null!;

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
        NlbBox.Text = item.Nlb;
        DefaultValueBox.Text = item.DefaultValue;

        MappingList.ItemsSource = _mappings;
        UpdateMappingEmptyState();
        UpdatePreview();
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
        _item.Nlb = NlbBox.Text ?? string.Empty;
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
        var nlb = NlbBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(nlb)) parts.Add($"NLB: {nlb}");
        var defVal = DefaultValueBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(defVal)) parts.Add($"Default: {defVal}");
        if (_mappings.Count > 0) parts.Add($"Mappings: {_mappings.Count}");

        PreviewToolTip.Text = parts.Count > 0 ? string.Join("\n", parts) : "(无额外信息)";
    }
}
