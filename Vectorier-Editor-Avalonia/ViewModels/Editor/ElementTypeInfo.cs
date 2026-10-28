using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier_Editor_Avalonia.Models.Scene;

namespace Vectorier_Editor_Avalonia.ViewModels.Editor;

/// <summary>One entry of the Element Bar (and of the Element / Create New menus).</summary>
public sealed partial class ElementTypeInfo : ObservableObject
{
    public ElementTypeInfo(ElementType type, int index)
    {
        Type = type;
        Index = index;
    }

    public ElementType Type { get; }

    /// <summary>Position in the Element menu (0-based). Number keys 1-9, 0 select the first ten.</summary>
    public int Index { get; }

    public string Name => Type switch
    {
        ElementType.BuildingIn => "In",
        ElementType.BuildingOut => "Out",
        _ => Type.ToString(),
    };

    public string ShortcutText => Index < 9 ? (Index + 1).ToString() : Index == 9 ? "0" : Index < 13 ? $"Shift+{Index - 9}" : string.Empty;

    public string ToolTip => string.IsNullOrEmpty(ShortcutText) ? Name : $"{Name} ({ShortcutText})";

    public StreamGeometry? Icon => GetIcon(Type);

    /// <summary>Visibility of this element type in the Scene (Element Bar checkbox).</summary>
    [ObservableProperty] private bool _isVisible = true;

    /// <summary>The type currently chosen in the Element Bar.</summary>
    [ObservableProperty] private bool _isActive;

    public static StreamGeometry? GetIcon(ElementType type)
    {
        string key = "Icon." + type switch
        {
            ElementType.Unknown => "Code",
            ElementType.BuildingIn or ElementType.BuildingOut => "Spawn",
            _ => type.ToString(),
        };
        return Application.Current != null && Application.Current.TryGetResource(key, null, out var res) ? res as StreamGeometry : null;
    }

    public static IReadOnlyList<ElementType> MenuOrder { get; } = new[]
    {
        ElementType.Object, ElementType.Image, ElementType.Trigger, ElementType.Area, ElementType.Platform,
        ElementType.Trapezoid, ElementType.Camera, ElementType.Spawn, ElementType.Model, ElementType.Item,
        ElementType.Animation, ElementType.Particle, ElementType.Comment,
    };
}
