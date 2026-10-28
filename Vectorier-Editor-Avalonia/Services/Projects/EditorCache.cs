using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Vectorier_Editor_Avalonia.Services.IO;

namespace Vectorier_Editor_Avalonia.Services.Projects;

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

public sealed class EditorCacheData
{
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

/// <summary>
/// Project-specific organisational data of the editor, kept in {project}/.vectorier/editor-cache.json.
/// It only organises the Project Window; it never affects the game files.
/// </summary>
public sealed class EditorCache
{
    private readonly IFileSystem _fs;
    private readonly string _path;
    private Task? _load;

    public EditorCache(IFileSystem fs, ProjectInfo project)
    {
        _fs = fs;
        _path = VPath.Combine(project.CacheFolder, "editor-cache.json");
    }

    public EditorCacheData Data { get; private set; } = new();

    public Task EnsureLoadedAsync() => _load ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            if (await _fs.FileExistsAsync(_path))
                Data = JsonSerializer.Deserialize(await _fs.ReadAllTextAsync(_path), EditorCacheJsonContext.Default.EditorCacheData) ?? new();
        }
        catch
        {
            Data = new EditorCacheData();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            await _fs.WriteAllTextAsync(_path, JsonSerializer.Serialize(Data, EditorCacheJsonContext.Default.EditorCacheData));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[EditorCache] save failed: " + ex.Message);
        }
    }

    // ================= TRIGGER VARIABLE TYPES ================= //

    /// <summary>Types cached for a trigger; looks in the level first, then in any level (copied or stored triggers).</summary>
    public Dictionary<string, string>? FindTriggerTypes(string level, string key)
    {
        if (Data.TriggerVariableTypes.TryGetValue(level, out var map) && map.TryGetValue(key, out var types))
            return types;
        foreach (var other in Data.TriggerVariableTypes.Values)
        {
            if (other.TryGetValue(key, out types))
                return types;
        }
        return null;
    }

    /// <summary>Replaces the level's entries with the given triggers' types (removes triggers that are gone).</summary>
    public void SetLevelTriggerTypes(string level, IEnumerable<(string Key, Dictionary<string, string> Types)> triggers)
    {
        var map = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (key, types) in triggers)
        {
            if (types.Count > 0)
                map[key] = new Dictionary<string, string>(types);
        }
        if (map.Count > 0) Data.TriggerVariableTypes[level] = map;
        else Data.TriggerVariableTypes.Remove(level);
    }

    /// <summary>Adds or replaces one trigger's types without touching the level's other entries.</summary>
    public void SetTriggerTypes(string level, string key, Dictionary<string, string> types)
    {
        if (!Data.TriggerVariableTypes.TryGetValue(level, out var map))
        {
            map = new Dictionary<string, Dictionary<string, string>>();
            Data.TriggerVariableTypes[level] = map;
        }
        if (types.Count > 0) map[key] = new Dictionary<string, string>(types);
        else map.Remove(key);
    }

    public List<CacheFolder> GetFolders(string context)
    {
        if (!Data.Folders.TryGetValue(context, out var folders))
        {
            folders = new List<CacheFolder>();
            Data.Folders[context] = folders;
        }
        return folders;
    }

    public string? FolderOf(string context, string item) =>
        GetFolders(context).FirstOrDefault(f => f.Items.Contains(item))?.Name;

    public void AssignToFolder(string context, string item, string? folderName)
    {
        foreach (var folder in GetFolders(context))
            folder.Items.Remove(item);
        if (folderName == null)
            return;
        var target = GetFolders(context).FirstOrDefault(f => f.Name == folderName);
        if (target == null)
        {
            target = new CacheFolder { Name = folderName };
            GetFolders(context).Add(target);
        }
        target.Items.Add(item);
    }

    /// <summary>"Name", "Name (1)", "Name (2)"... unique among the context's folders.</summary>
    public string UniqueFolderName(string context, string baseName)
    {
        var names = GetFolders(context).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
            return baseName;
        for (int i = 1; ; i++)
        {
            string candidate = $"{baseName} ({i})";
            if (!names.Contains(candidate))
                return candidate;
        }
    }
}
