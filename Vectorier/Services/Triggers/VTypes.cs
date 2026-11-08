// Ported from the Unity Vectorier Editor (Scripts/Trigger/TriggerCatalog.cs).
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vectorier.Services.Triggers;

// ================= Value types =================

// Used by both the Variables panel and the attribute slots.
// Number and Spawn are slot only, the panel only offers VariableTypes.
public enum VType { String, AI, Animation, Bool, Int, Key, Model, Orientation, Sound, UInt, Zoom, Color, Number, Spawn }

public static class VTypes
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly VType[] VariableTypes =
    {
        VType.AI, VType.Animation, VType.Bool, VType.Color, VType.Int, VType.Key,
        VType.Model, VType.Orientation, VType.Sound, VType.String, VType.UInt, VType.Zoom
    };

    public static readonly string[] VariableTypeLabels;

    public static readonly string[] Keys = { "Up", "Down", "Left", "Right" };
    public static readonly string[] Orientations = { "None", "Vertical", "Horizontal" };

    public static readonly string[] ZoomLabels = { "Max (1.1)", "Normal (1)", "Reduced (0.8)", "Min (0.65)", "Custom" };
    public static readonly string[] ZoomValues = { "1.1", "1", "0.8", "0.65" };

    public static readonly string[] AILabels = { "Any (-1)", "Player (0)", "Hunter (1)", "Custom" };
    public static readonly string[] AIValues = { "-1", "0", "1" };

    public const string White = "#FFFFFFFF";
    public const string NoModel = "None";

    static VTypes()
    {
        VariableTypeLabels = new string[VariableTypes.Length];
        for (int i = 0; i < VariableTypes.Length; i++) VariableTypeLabels[i] = VariableTypes[i].ToString();
    }

    public enum Family { Text, Number, Bool, Color }

    public static Family FamilyOf(VType t)
    {
        switch (t)
        {
            case VType.AI:
            case VType.Int:
            case VType.UInt:
            case VType.Number:
            case VType.Zoom: return Family.Number;
            case VType.Bool: return Family.Bool;
            case VType.Color: return Family.Color;
            default: return Family.Text;
        }
    }

    // label for the literal option in a slot's mode popup
    public static string SlotLabel(VType t)
    {
        if (t == VType.Model) return "Model";
        switch (FamilyOf(t))
        {
            case Family.Number: return "Number";
            case Family.Bool: return "Boolean";
            case Family.Color: return "Color";
            default: return "Text";
        }
    }

    public static string Default(VType t)
    {
        switch (t)
        {
            case VType.AI: return "-1";
            case VType.Bool: return "0";
            case VType.Int:
            case VType.UInt:
            case VType.Number: return "0";
            case VType.Key: return "Up";
            case VType.Model: return NoModel;
            case VType.Orientation: return "None";
            case VType.Zoom: return "1";
            case VType.Color: return White;
            default: return "";
        }
    }

    // falls back to a sane default when v doesn't fit the type
    public static string Coerce(VType t, string v, IList<string> models = null)
    {
        v = v ?? "";
        int n;
        float f;

        switch (t)
        {
            case VType.AI:
                return TryInt(v, out n) ? Math.Max(-1, n).ToString(Inv) : "-1";

            case VType.Bool:
                return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) ? "1" : "0";

            case VType.Int:
                return TryInt(v, out n) ? n.ToString(Inv) : "0";

            case VType.UInt:
                return TryInt(v, out n) ? Math.Max(0, n).ToString(Inv) : "0";

            case VType.Number:
                return TryFloat(v, out f) ? Num(f) : "0";

            case VType.Zoom:
                return TryFloat(v, out f) && f > 0f ? Num(f) : "1";

            case VType.Key:
                {
                    int i = IndexOf(Keys, v, true);
                    return i >= 0 ? Keys[i] : "Up";
                }

            case VType.Orientation:
                {
                    int i = IndexOf(Orientations, v, true);
                    return i >= 0 ? Orientations[i] : "None";
                }

            case VType.Model:
                if (models != null && models.Count > 0) return IndexOf(models, v, false) >= 0 ? v : models[0];
                return string.IsNullOrEmpty(v) ? NoModel : v;

            case VType.Color:
                return NormalizeHex(v) ?? White;

            default:
                return v;
        }
    }

    public static bool TryInt(string s, out int n)
    {
        if (int.TryParse(s, NumberStyles.Integer, Inv, out n)) return true;
        float f;
        if (TryFloat(s, out f) && f > int.MinValue && f < int.MaxValue) { n = (int)Math.Round(f); return true; }
        n = 0;
        return false;
    }

    public static bool TryFloat(string s, out float f)
    {
        return float.TryParse(s, NumberStyles.Float, Inv, out f);
    }

    public static string Num(float f) { return f.ToString(Inv); }

    // takes RRGGBB or RRGGBBAA (# is optional), returns null if its not a colour
    public static string NormalizeHex(string v)
    {
        if (string.IsNullOrEmpty(v)) return null;
        string s = v.Trim().TrimStart('#');
        if (s.Length == 6) s += "FF";
        if (s.Length != 8) return null;
        for (int i = 0; i < s.Length; i++)
            if (!Uri.IsHexDigit(s[i])) return null;
        return "#" + s.ToUpperInvariant();
    }

    // templates also use integer/boolean/float/text, map those first
    public static VType FromName(string s)
    {
        if (string.IsNullOrEmpty(s)) return VType.String;
        switch (s.Trim().ToLowerInvariant())
        {
            case "integer": return VType.Int;
            case "boolean": return VType.Bool;
            case "float": return VType.Number;
            case "text": return VType.String;
        }
        VType t;
        return Enum.TryParse(s.Trim(), true, out t) ? t : VType.String;
    }

    public static int IndexOf(IList<string> list, string v, bool ignoreCase)
    {
        if (list == null) return -1;
        var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], v, cmp)) return i;
        return -1;
    }
}
