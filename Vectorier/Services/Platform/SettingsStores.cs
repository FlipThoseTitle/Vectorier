using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Vectorier.Services.Platform;

/// <summary>Small JSON key/value settings file.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values;

    public JsonSettingsStore(string path)
    {
        _path = path;
        _values = new Dictionary<string, string>();
        try
        {
            if (File.Exists(path))
                _values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch
        {
            _values = new Dictionary<string, string>();
        }
    }

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string? value)
    {
        if (value == null)
            _values.Remove(key);
        else
            _values[key] = value;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_values));
        }
        catch
        {
            // Settings are a convenience; failure to save must not break the editor.
        }
    }
}

/// <summary>Settings kept only for the current session.</summary>
public sealed class MemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = new();
    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, string? value)
    {
        if (value == null) _values.Remove(key);
        else _values[key] = value;
    }
}
