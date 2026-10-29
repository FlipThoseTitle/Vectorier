using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Vectorier.Controls;

/// <summary>int ↔ decimal? for NumericUpDown.</summary>
public sealed class IntToDecimalConverter : IValueConverter
{
    public static readonly IntToDecimalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? (decimal?)i : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? (int)Math.Round(d) : 0;
}

/// <summary>double ↔ decimal? for NumericUpDown.</summary>
public sealed class DoubleToDecimalConverter : IValueConverter
{
    public static readonly DoubleToDecimalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d && !double.IsNaN(d) && !double.IsInfinity(d))
        {
            try { return (decimal?)Math.Round(d, 5); }
            catch { return null; }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? (double)d : 0.0;
}

/// <summary>Mode label for headers: "Hunter Mode" when true, "Common Mode" otherwise.</summary>
public sealed class HunterModeLabelConverter : IValueConverter
{
    public static readonly HunterModeLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Hunter Mode Settings" : "Common Mode Settings";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Avalonia Color ↔ Color (for ColorPicker two-way bindings) passthrough with null safety.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public static readonly ColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color c ? new SolidColorBrush(c) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SolidColorBrush b ? b.Color : Colors.White;
}

/// <summary>True when the value's text equals the converter parameter (enum names, numbers...).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
