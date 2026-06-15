using System.Windows;
using EAPSimulator.Wpf.ViewModels;

namespace EAPSimulator.Wpf.Views;

public partial class FieldEditDialog : Window
{
    private readonly SecsItemViewModel _item;

    public FieldEditDialog(SecsItemViewModel item)
    {
        InitializeComponent();
        _item = item;

        // Load current values
        TypeNameCombo.SelectedItem = item.TypeName;
        AliasBox.Text = item.Alias;
        DescriptionBox.Text = item.Description;
        ValueText.Text = item.ValueText;
        FormatBox.Text = item.Format;
        NlbBox.Text = item.Nlb;
        DefaultValueBox.Text = item.DefaultValue;

        // Load value mappings
        foreach (var m in item.ValueMappings)
            MappingList.Items.Add(new ValueMappingEntry { Value = m.Value, DisplayText = m.DisplayText });
    }

    private void OnAddMapping(object sender, RoutedEventArgs e)
    {
        MappingList.Items.Add(new ValueMappingEntry());
    }

    private void OnRemoveMapping(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is ValueMappingEntry entry)
            MappingList.Items.Remove(entry);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        _item.TypeName = TypeNameCombo.SelectedItem?.ToString() ?? "A";
        _item.Alias = AliasBox.Text;
        _item.Description = DescriptionBox.Text;
        _item.ValueText = ValueText.Text;
        _item.Format = FormatBox.Text;
        _item.Nlb = NlbBox.Text;
        _item.DefaultValue = DefaultValueBox.Text;

        _item.ValueMappings.Clear();
        foreach (var obj in MappingList.Items)
        {
            if (obj is ValueMappingEntry entry)
                _item.ValueMappings.Add(entry);
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
