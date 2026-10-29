using System;
using System.Globalization;

namespace Vectorier.Services;

/// <summary>Where Trigger/Area labels are anchored inside their rectangle (Unity's TextAnchor).</summary>
public enum LabelAnchor
{
    UpperLeft,
    UpperCenter,
    UpperRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    LowerLeft,
    LowerCenter,
    LowerRight
}

/// <summary>Editor preferences (Unity's Preference window plus the theme), persisted in the settings store.</summary>
public static class EditorPreferences
{
    private const string KeyShowOutline = "Pref.ShowOutline";
    private const string KeyShowPlatformOutline = "Pref.ShowPlatformOutline";
    private const string KeyShowTriggerText = "Pref.ShowTriggerText";
    private const string KeyShowAreaText = "Pref.ShowAreaText";
    private const string KeyShowAreaType = "Pref.ShowAreaType";
    private const string KeyTextAnchor = "Pref.TextAnchor";
    private const string KeyRenameDuplicate = "Pref.RenameDuplicate";

    public static event EventHandler? Changed;

    private static string? Get(string key) => AppServices.Platform?.Settings.Get(key);

    private static void Set(string key, string value)
    {
        AppServices.Platform?.Settings.Set(key, value);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool GetBool(string key, bool fallback) => Get(key) is { } s ? s == "1" : fallback;
    private static void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

    public static string Theme
    {
        get => Get("Theme") ?? "Light";
        set
        {
            AppServices.Platform?.Settings.Set("Theme", value);
            App.ApplyTheme(value);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Outline around Trigger and Area rectangles.</summary>
    public static bool ShowOutline
    {
        get => GetBool(KeyShowOutline, true);
        set => SetBool(KeyShowOutline, value);
    }

    public static bool ShowPlatformOutline
    {
        get => GetBool(KeyShowPlatformOutline, true);
        set => SetBool(KeyShowPlatformOutline, value);
    }

    public static bool ShowTriggerText
    {
        get => GetBool(KeyShowTriggerText, true);
        set => SetBool(KeyShowTriggerText, value);
    }

    public static bool ShowAreaText
    {
        get => GetBool(KeyShowAreaText, true);
        set => SetBool(KeyShowAreaText, value);
    }

    /// <summary>Area labels show their type: "CoolArea [Catch]" instead of "CoolArea".</summary>
    public static bool ShowAreaType
    {
        get => GetBool(KeyShowAreaType, true);
        set => SetBool(KeyShowAreaType, value);
    }

    public static LabelAnchor TextAnchor
    {
        get => Enum.TryParse<LabelAnchor>(Get(KeyTextAnchor), out var a) ? a : LabelAnchor.UpperLeft;
        set => Set(KeyTextAnchor, value.ToString());
    }

    /// <summary>Duplicates keep the original name instead of getting a " (1)" suffix.</summary>
    public static bool RenameDuplicate
    {
        get => GetBool(KeyRenameDuplicate, true);
        set => SetBool(KeyRenameDuplicate, value);
    }

    public static double GetDouble(string key, double fallback) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    public static void SetDouble(string key, double value) =>
        AppServices.Platform?.Settings.Set(key, value.ToString(CultureInfo.InvariantCulture));
}
