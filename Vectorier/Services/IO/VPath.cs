using System;
using System.Collections.Generic;

namespace Vectorier.Services.IO;

public static class VPath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        path = path.Replace('\\', '/');
        while (path.Contains("//"))
            path = path.Replace("//", "/");
        return path.Trim('/');
    }

    public static string Combine(params string[] parts)
    {
        var list = new List<string>();
        foreach (var part in parts)
        {
            var normalized = Normalize(part);
            if (normalized.Length > 0)
                list.Add(normalized);
        }
        return string.Join('/', list);
    }

    public static string GetFileName(string path)
    {
        path = Normalize(path);
        int index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }

    public static string GetFileNameWithoutExtension(string path)
    {
        string name = GetFileName(path);
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    public static string GetExtension(string path)
    {
        string name = GetFileName(path);
        int dot = name.LastIndexOf('.');
        return dot < 0 ? string.Empty : name[dot..];
    }

    public static string GetDirectory(string path)
    {
        path = Normalize(path);
        int index = path.LastIndexOf('/');
        return index < 0 ? string.Empty : path[..index];
    }

    public static string ChangeExtension(string path, string extension)
    {
        string dir = GetDirectory(path);
        string name = GetFileNameWithoutExtension(path) + extension;
        return Combine(dir, name);
    }

    public static bool HasExtension(string path, params string[] extensions)
    {
        string ext = GetExtension(path);
        foreach (var e in extensions)
        {
            if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
