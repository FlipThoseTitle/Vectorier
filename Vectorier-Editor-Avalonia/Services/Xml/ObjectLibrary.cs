using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services.IO;

namespace Vectorier_Editor_Avalonia.Services.Xml;

/// <summary>
/// Loads object definitions ("prefabs") from the XML files in a project's xmlroot folder and resolves the
/// definitions visible to a level through its &lt;Sets&gt;, the same way the game does:
/// Ground files first (plus the Sets referenced by City files), then City files, later files overriding earlier ones.
/// </summary>
public sealed class ObjectLibrary
{
    private readonly IFileSystem _fs;
    private readonly string _xmlRoot;
    private readonly Dictionary<string, CachedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    private sealed class CachedFile
    {
        public DateTimeOffset? Stamp;
        public List<string> NestedSets = new();
        public Dictionary<string, ObjectDefinition> Definitions = new(StringComparer.Ordinal);
        public List<ObjectDefinition> Ordered = new();
    }

    public ObjectLibrary(IFileSystem fileSystem, string xmlRootDirectory)
    {
        _fs = fileSystem;
        _xmlRoot = xmlRootDirectory;
    }

    public string XmlRoot => _xmlRoot;

    /// <summary>Definitions of one file, in file order (used by the Project Window).</summary>
    public async Task<IReadOnlyList<ObjectDefinition>> GetFileDefinitionsAsync(string fileName)
    {
        var file = await LoadFileAsync(fileName);
        return file?.Ordered ?? new List<ObjectDefinition>();
    }

    /// <summary>An Object XML is a Building file when any definition has In / Out / Box attributes.</summary>
    public async Task<bool> IsBuildingFileAsync(string fileName)
    {
        var defs = await GetFileDefinitionsAsync(fileName);
        return defs.Any(d => d.IsBuilding);
    }

    public void Invalidate(string? fileName = null)
    {
        if (fileName == null)
            _files.Clear();
        else
            _files.Remove(fileName);
    }

    private async Task<CachedFile?> LoadFileAsync(string fileName)
    {
        string path = VPath.Combine(_xmlRoot, fileName);
        await _lock.WaitAsync();
        try
        {
            var stamp = await _fs.GetLastWriteTimeAsync(path);
            if (stamp == null)
                return null;

            if (_files.TryGetValue(fileName, out var cached) && cached.Stamp == stamp)
                return cached;

            var data = await _fs.ReadAllBytesAsync(path);
            var parsed = await Task.Run(() => Parse(fileName, data));
            parsed.Stamp = stamp;
            _files[fileName] = parsed;
            return parsed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ObjectLibrary] Failed to load {fileName}: {ex.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static CachedFile Parse(string fileName, byte[] data)
    {
        var result = new CachedFile();
        var doc = XmlHelpers.ParseDocument(data);
        var root = doc.Root;
        if (root == null)
            return result;

        var sets = root.Element("Sets");
        if (sets != null)
        {
            foreach (var set in sets.Elements())
            {
                if (set.Name.LocalName is "Ground" or "City")
                {
                    string? setFile = set.Attribute("FileName")?.Value;
                    if (!string.IsNullOrWhiteSpace(setFile))
                        result.NestedSets.Add(setFile);
                }
            }
        }

        var objects = root.Element("Objects");
        if (objects == null)
            return result;

        foreach (var obj in objects.Elements("Object"))
        {
            string? name = obj.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var def = new ObjectDefinition(name, fileName, obj);
            result.Definitions[name] = def;
            result.Ordered.Add(def);
        }
        return result;
    }

    /// <summary>Builds the name → definition table for a level's sets.</summary>
    public async Task<DefinitionTable> ResolveSetsAsync(IEnumerable<string> groundFiles, IEnumerable<string> cityFiles)
    {
        var objectFiles = new List<string>();
        foreach (var file in groundFiles)
        {
            if (!objectFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                objectFiles.Add(file);
        }

        var cityLoaded = new List<CachedFile>();
        foreach (var city in cityFiles)
        {
            var loaded = await LoadFileAsync(city);
            if (loaded == null)
                continue;
            cityLoaded.Add(loaded);
            foreach (var nested in loaded.NestedSets)
            {
                if (!objectFiles.Contains(nested, StringComparer.OrdinalIgnoreCase))
                    objectFiles.Add(nested);
            }
        }

        var table = new DefinitionTable();
        foreach (var file in objectFiles)
        {
            var loaded = await LoadFileAsync(file);
            if (loaded == null)
            {
                table.MissingFiles.Add(file);
                continue;
            }
            foreach (var def in loaded.Ordered)
                table.Definitions[def.Name] = def;
        }
        foreach (var loaded in cityLoaded)
        {
            foreach (var def in loaded.Ordered)
                table.Definitions[def.Name] = def;
        }
        return table;
    }

    /// <summary>
    /// Definition table for an objects file opened in the Project Window: its own definitions plus those of
    /// the files it references through Sets (its own entries win).
    /// </summary>
    public async Task<DefinitionTable> ResolveForFileAsync(string fileName)
    {
        var file = await LoadFileAsync(fileName);
        var table = new DefinitionTable();
        if (file == null)
            return table;

        foreach (var nested in file.NestedSets)
        {
            var nestedFile = await LoadFileAsync(nested);
            if (nestedFile == null)
                continue;
            foreach (var def in nestedFile.Ordered)
                table.Definitions[def.Name] = def;
        }
        foreach (var def in file.Ordered)
            table.Definitions[def.Name] = def;
        return table;
    }
}

/// <summary>Name → definition lookup for one level.</summary>
public sealed class DefinitionTable
{
    public Dictionary<string, ObjectDefinition> Definitions { get; } = new(StringComparer.Ordinal);
    public List<string> MissingFiles { get; } = new();

    public ObjectDefinition? Find(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return Definitions.TryGetValue(name, out var def) ? def : null;
    }
}
