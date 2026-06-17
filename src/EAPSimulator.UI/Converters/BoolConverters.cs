using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EAPSimulator.UI.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public string TrueBrush { get; set; } = "#E8F5E9";
    public string FalseBrush { get; set; } = "#FFEBEE";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var hex = b ? TrueBrush : FalseBrush;
            try
            {
                return SolidColorBrush.Parse(hex);
            }
            catch
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "是";
    public string FalseText { get; set; } = "否";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueText : FalseText;
        return FalseText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
