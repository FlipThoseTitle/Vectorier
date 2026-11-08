using System;
using System.Globalization;

namespace Vectorier.Models.Scene;

/// <summary>Number helpers that always use the invariant culture.</summary>
public static class Num
{
    public static string Format(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";
        // Remove float noise such as 12.000000001 while keeping meaningful decimals.
        double rounded = Math.Round(value, 5);
        if (rounded == 0)
            rounded = 0; // normalizes -0
        return rounded.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    public static string FormatMatrix(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    public static double Parse(string? text, double fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
    }

    public static int ParseInt(string? text, int fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return value;
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return (int)Math.Round(d);
        return fallback;
    }
}
