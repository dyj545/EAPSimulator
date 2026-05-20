using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace EAPSimulator.UI.Converters;

/// <summary>
/// Converts a ListBoxItem to its 1-based index string (e.g. "#1", "#2").
/// Usage: {Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Converter={x:Static converters:ListBoxIndexConverter.Instance}}
/// </summary>
public class ListBoxIndexConverter : IValueConverter
{
    public static readonly ListBoxIndexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ListBoxItem item)
        {
            var listBox = ItemsControl.ItemsControlFromItemContainer(item);
            if (listBox != null)
            {
                var index = listBox.IndexFromContainer(item);
                return index >= 0 ? $"#{index + 1}" : "";
            }
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
