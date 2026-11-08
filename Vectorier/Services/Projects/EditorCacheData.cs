using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Vectorier.Services.Projects;

/// <summary>A virtual folder of the Project Window (textures, triggers, areas, models...).</summary>
public sealed class CacheFolder
{
    public string Name { get; set; } = "New Folder";

    /// <summary>Item keys inside the folder (texture names, stored trigger ids, model file names).</summary>
    public List<string> Items { get; set; } = new();
}

/// <summary>A Trigger stored in the Project Window (name, size and content).</summary>
public sealed class StoredTrigger
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Trigger";
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 100;
    public string Content { get; set; } = string.Empty;
}

/// <summary>An Area stored in the Project Window (name, size and Area component values).</summary>
public sealed class StoredArea
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Area";
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 100;
    public string AreaType { get; set; } = "Animation";
    public int Distance { get; set; } = 300;
    public string ItemName { get; set; } = "TRICK_";
    public int Score { get; set; } = 100;
    public string Key { get; set; } = "Up";
    public string Description { get; set; } = string.Empty;
}

/// <summary>A Platform's Rough / Smooth / Block settings, found again by its level-space rectangle.</summary>
public sealed class StoredSurface
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Type { get; set; } = "Rough";
    public bool Floor { get; set; } = true;
    public bool LeftWall { get; set; } = true;
    public bool RightWall { get; set; } = true;
    public int Seed { get; set; } = 1;
}

public sealed class EditorCacheData
{
    /// <summary>
    /// Platform surface settings per level (or "object:file:name" for object definitions). The game XML has no
    /// place for them; the generated textures are saved as ordinary images.
    /// </summary>
    public Dictionary<string, List<StoredSurface>> PlatformSurfaces { get; set; } = new();

    /// <summary>Folders per Project Window context ("Image", "Trigger", "Area", "Model", "Object").</summary>
    public Dictionary<string, List<CacheFolder>> Folders { get; set; } = new();

    /// <summary>Display order of the objects XML files in the Object context.</summary>
    public List<string> ObjectFileOrder { get; set; } = new();

    public List<StoredTrigger> Triggers { get; set; } = new();
    public List<StoredArea> Areas { get; set; } = new();

    /// <summary>Moves XML chosen for the Move Visualizer / animations page.</summary>
    public string? MovesFile { get; set; }

    /// <summary>
    /// Trigger Editor variable types (Unity's TriggerComponent.variableTypes), per level and per trigger.
    /// The game XML has no place for them, so they are cached here: level → trigger key → (variable → type).
    /// </summary>
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> TriggerVariableTypes { get; set; } = new();
}

[JsonSerializable(typeof(EditorCacheData))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class EditorCacheJsonContext : JsonSerializerContext
{
}
