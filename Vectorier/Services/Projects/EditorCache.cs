using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vectorier.Services.IO;

namespace Vectorier.Services.Projects;

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
