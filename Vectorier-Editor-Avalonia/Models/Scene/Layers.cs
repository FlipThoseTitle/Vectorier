using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vectorier_Editor_Avalonia.Models.Scene;

/// <summary>
/// Readable names for Vector's parallax <c>Factor</c> values (see the Unity project's Parallax folder).
/// The XML always stores the factor; names are only for the UI.
/// </summary>
public static class Layers
{
    public const string Default = "Default";

    /// <summary>Named layers in display order. "Default" is equivalent to "Gameplay (Default)".</summary>
    public static readonly IReadOnlyList<(string Name, double Factor)> Named = new List<(string, double)>
    {
        (Default,              1.0),
        ("Background",         0.05),
        ("Far Background",     0.1),
        ("Midground",          0.25),
        ("Backdrop",           0.5),
        ("Near Backdrop",      0.7),
        ("Gameplay (Default)", 1.0),
        ("Overlay",            1.00001),
        ("Foreground",         1.25),
        ("Near Foreground",    1.5),
        ("Frontmost",          2.0),
    };

    public static readonly IReadOnlyList<string> Names = BuildNames();

    private static List<string> BuildNames()
    {
        var names = new List<string>();
        foreach (var (name, _) in Named)
            names.Add(name);
        return names;
    }

    private const double Tolerance = 1e-9;

    /// <summary>Factor of a layer name: a named layer, a plain number ("0.33"), or 1.</summary>
    public static double GetFactor(string? layer)
    {
        layer = layer?.Trim();
        if (string.IsNullOrEmpty(layer))
            return 1.0;

        foreach (var (name, factor) in Named)
        {
            if (string.Equals(name, layer, StringComparison.OrdinalIgnoreCase))
                return factor;
        }

        return double.TryParse(layer, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 1.0;
    }

    /// <summary>Layer name for a factor found in XML. Factor 1 maps to "Default"; unknown factors keep their number.</summary>
    public static string GetLayerName(string? factorText)
    {
        if (string.IsNullOrWhiteSpace(factorText))
            return Default;

        if (!double.TryParse(factorText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double factor))
            return Default;

        if (Math.Abs(factor - 1.0) < Tolerance)
            return Default;

        foreach (var (name, value) in Named)
        {
            if (Math.Abs(value - factor) < Tolerance)
                return name;
        }

        return factorText.Trim();
    }

    /// <summary>Factor written to XML for a layer name, formatted the way Vector expects ("0.5", "1.00001").</summary>
    public static string GetFactorText(string? layer) => Num.Format(GetFactor(layer));

    /// <summary>
    /// Scene-view parallax for a factor, using the Unity Parallax component defaults with a fixed zoom of 1
    /// (baseZoom 0.5, frameScaleMultiplier 2). Screen = offset + layerPosition * frameScale, with
    /// offset = center - center * factor * frameScale.
    /// </summary>
    public static double GetFrameScale(double factor)
    {
        const double baseZoom = 0.5;
        const double zoomValue = 1.0;
        const double frameScaleMultiplier = 2.0;

        double effectiveZoom = baseZoom * zoomValue;
        double scale = 1.0;
        if (effectiveZoom > 0)
        {
            double denominator = (1.0 / effectiveZoom - 1.0) * factor + 1.0;
            scale = Math.Abs(denominator) < 1e-9 ? 1.0 : 1.0 / denominator;
        }

        scale = Math.Round(scale, 1, MidpointRounding.AwayFromZero);
        return Math.Round(scale * frameScaleMultiplier, 1);
    }
}

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
