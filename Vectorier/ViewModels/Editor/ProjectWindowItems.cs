using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Models.Scene;
using Vectorier.Services.Assets;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.Editor;

public enum PwContext
{
    /// <summary>Premade objects from xmlroot files.</summary>
    Objects,
    /// <summary>Textures with cached virtual folders (Image, Animation, Particle).</summary>
    Textures,
    Triggers,
    Areas,
    Models,
    Items,
    /// <summary>Platform surfaces: Rough, Smooth and Block (dressed with block textures).</summary>
    Platforms,
    /// <summary>Trapezoid, Camera, Spawn, Comment: nothing to browse.</summary>
    None
}

/// <summary>How a click changes the Project Window selection (Ctrl toggles, Shift selects a range).</summary>
public enum PwSelect
{
    Replace,
    Toggle,
    Range,
}

/// <summary>Several selected tiles dragged together (to a folder; never placed in the Scene).</summary>
public sealed record PwItemGroup(IReadOnlyList<PwItem> Items);

/// <summary>Left panel entry: an objects XML file, a cached folder, or the Unorganized bucket.</summary>
public sealed partial class PwFolder : ObservableObject
{
    public PwFolder(string name, string? fileName = null, bool isUnorganized = false)
    {
        Name = name;
        FileName = fileName;
        IsUnorganized = isUnorganized;
    }

    public string Name { get; }

    /// <summary>For the Objects context: the XML file name inside xmlroot.</summary>
    public string? FileName { get; }

    public bool IsUnorganized { get; }

    /// <summary>Name shown in the folder list ("All" / "Unorganized" are translated).</summary>
    public string DisplayName => IsUnorganized ? Loc.T(Name) : Name;

    public bool IsFile => FileName != null;

    public bool IsUserFolder => !IsUnorganized && FileName == null;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Object XML type (null until checked): Building files use In / Out / Box attributes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeBadge), nameof(HasTypeBadge))]
    private bool? _isBuilding;

    public string TypeBadge => IsBuilding == true ? Loc.T("Building") : Loc.T("Object");
    public bool HasTypeBadge => IsFile && IsBuilding != null;
}

/// <summary>Right panel entry.</summary>
public sealed partial class PwItem : ObservableObject
{
    public PwItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>Identity used by cached folders.</summary>
    public string Key { get; }

    public string Label { get; }

    public ObjectDefinition? Definition { get; init; }
    public TextureEntry? Texture { get; init; }
    public StoredTrigger? Trigger { get; init; }
    public StoredArea? Area { get; init; }
    public string? ModelName { get; init; }
    public ItemType? ItemType { get; init; }
    public PlatformSurfaceType? PlatformSurface { get; init; }

    /// <summary>Texture shown as preview (textures and items).</summary>
    public string? PreviewTexture { get; init; }

    [ObservableProperty] private bool _isSelected;

    /// <summary>False for models that cannot be rendered (they cannot be placed either).</summary>
    [ObservableProperty] private bool _canPlace = true;

    [ObservableProperty] private string? _status;

    /// <summary>Model preview (filled lazily when the tile becomes visible).</summary>
    [ObservableProperty] private Geometry? _silhouette;

    public bool PreviewRequested { get; set; }

    public bool ShowModelPlaceholder => ModelName != null && Silhouette == null;

    partial void OnSilhouetteChanged(Geometry? value) => OnPropertyChanged(nameof(ShowModelPlaceholder));

    public string ToolTip => Status is { Length: > 0 } s ? $"{Label}\n{s}" : Label;

    partial void OnStatusChanged(string? value) => OnPropertyChanged(nameof(ToolTip));
}
