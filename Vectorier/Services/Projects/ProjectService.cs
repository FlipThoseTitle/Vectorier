using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;

namespace Vectorier.Services.Projects;

/// <summary>A project folder inside Projects.</summary>
public sealed class ProjectInfo
{
    public ProjectInfo(string name, string path, string displayPath)
    {
        Name = name;
        Path = path;
        DisplayPath = displayPath;
    }

    public string Name { get; }

    /// <summary>Relative path ("Projects/Name").</summary>
    public string Path { get; }

    /// <summary>Directory shown in the project list.</summary>
    public string DisplayPath { get; }

    public string XmlRoot => VPath.Combine(Path, "xmlroot");
    public string LevelsFolder => VPath.Combine(Path, "xmlroot", "levels");
    public string TexturesFolder => VPath.Combine(Path, "textures");
    public string AnimatedTexturesFolder => VPath.Combine(Path, "animatedtextures");
    public string ModelsFolder => VPath.Combine(Path, "models");
    public string AnimationsFolder => VPath.Combine(Path, "animations");
    public string CacheFolder => VPath.Combine(Path, ".vectorier");

    public override string ToString() => Name;
}

/// <summary>Creates, lists, renames and deletes projects, and edits their metadata files.</summary>
public sealed class ProjectService
{
    public const string ProjectsFolder = "Projects";
    public const string TemplateName = "_ProjectTemplate";
    public const string AuthorFile = "author.txt";
    public const string DescriptionFile = "description.txt";
    public const string ThumbnailFile = "thumbnail.png";

    private static readonly string[] ThumbnailExtensions = { ".png", ".jpg", ".jpeg" };

    // Folders whose .gitkeep placeholders are removed after copying the template (as in Unity).
    private static readonly string[] GitKeepFolders = { "animations/data", "icons/locations", "icons/stories", "xmlroot/levels" };

    private readonly IFileSystem _fs;

    public ProjectService(IFileSystem fileSystem)
    {
        _fs = fileSystem;
    }

    public IFileSystem FileSystem => _fs;

    public ProjectInfo GetProject(string name)
    {
        string path = VPath.Combine(ProjectsFolder, name);
        return new ProjectInfo(name, path, _fs.GetNativePath(path) ?? $"{_fs.RootDisplayName}/{path}");
    }

    public async Task<List<ProjectInfo>> GetProjectsAsync()
    {
        await _fs.CreateDirectoryAsync(ProjectsFolder);
        var dirs = await _fs.GetDirectoriesAsync(ProjectsFolder);
        return dirs
            .Select(VPath.GetFileName)
            .Where(n => !string.Equals(n, TemplateName, StringComparison.OrdinalIgnoreCase) && !n.StartsWith('.'))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(GetProject)
            .ToList();
    }

    public Task<bool> TemplateExistsAsync() => _fs.DirectoryExistsAsync(VPath.Combine(ProjectsFolder, TemplateName));

    // ================= NAMES ================= //

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Returns an error message, or null when the name can be used as a folder name on every target platform.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "The project name cannot be empty.";

        if (name != name.Trim())
            return "The project name cannot start or end with spaces.";

        if (name.EndsWith('.'))
            return "The project name cannot end with a dot.";

        if (name is "." or "..")
            return "This name is not allowed.";

        if (string.Equals(name, TemplateName, StringComparison.OrdinalIgnoreCase))
            return $"'{TemplateName}' is reserved for the project template.";

        const string invalid = "<>:\"/\\|?*";
        foreach (char c in name)
        {
            if (c < 32 || invalid.Contains(c))
                return $"The character '{(c < 32 ? "control character" : c.ToString())}' cannot be used in a project name.";
        }

        string stem = name.Split('.')[0];
        if (ReservedWindowsNames.Contains(stem))
            return $"'{name}' is a reserved name on Windows.";

        if (name.Length > 100)
            return "The project name is too long (100 characters at most).";

        return null;
    }

    /// <summary>"Name", or "Name (1)", "Name (2)"... whichever does not exist yet.</summary>
    public async Task<string> GetUniqueNameAsync(string baseName)
    {
        var existing = (await _fs.GetDirectoriesAsync(ProjectsFolder))
            .Select(VPath.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseName))
            return baseName;

        for (int i = 1; ; i++)
        {
            string candidate = $"{baseName} ({i})";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    // ================= OPERATIONS ================= //

    public async Task<ProjectInfo> CreateAsync(string requestedName, IProgress<string>? progress = null)
    {
        string? error = ValidateName(requestedName);
        if (error != null)
            throw new InvalidOperationException(error);

        string name = await GetUniqueNameAsync(requestedName);
        string target = VPath.Combine(ProjectsFolder, name);
        string template = VPath.Combine(ProjectsFolder, TemplateName);

        if (await _fs.DirectoryExistsAsync(template))
        {
            await _fs.CopyDirectoryAsync(template, target, progress);
            foreach (var folder in GitKeepFolders)
                await _fs.DeleteFileAsync(VPath.Combine(target, folder, ".gitkeep"));
        }
        else
        {
            await _fs.CreateDirectoryAsync(target);
        }

        return GetProject(name);
    }

    public async Task<ProjectInfo> DuplicateAsync(ProjectInfo project, IProgress<string>? progress = null)
    {
        string name = await GetUniqueNameAsync(project.Name);
        await _fs.CopyDirectoryAsync(project.Path, VPath.Combine(ProjectsFolder, name), progress);
        return GetProject(name);
    }

    public Task DeleteAsync(ProjectInfo project) => _fs.DeleteDirectoryAsync(project.Path);

    /// <summary>Copies a project into {destination}/{project name}, without the .vectorier cache folder.</summary>
    public async Task PublishAsync(ProjectInfo project, IStorageFolder destination, IProgress<string>? progress = null)
    {
        string? source = _fs.GetNativePath(project.Path);
        string? local = destination.TryGetLocalPath();
        if (source != null && local != null)
        {
            string target = System.IO.Path.Combine(local, project.Name);
            await Task.Run(() => FastCopy.CopyDirectory(source, target, progress, FastCopy.IsCachePath));
            return;
        }
        var folder = await FastCopy.GetOrCreateFolderAsync(destination, project.Name);
        await FastCopy.CopyToStorageFolderAsync(_fs, project.Path, folder, progress, FastCopy.IsCachePath);
    }

    public async Task<ProjectInfo> RenameAsync(ProjectInfo project, string newName)
    {
        string? error = ValidateName(newName);
        if (error != null)
            throw new InvalidOperationException(error);

        if (string.Equals(project.Name, newName, StringComparison.Ordinal))
            return project;

        string target = VPath.Combine(ProjectsFolder, newName);
        bool caseOnly = string.Equals(project.Name, newName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && await _fs.DirectoryExistsAsync(target))
            throw new InvalidOperationException(Loc.F("A project named '{0}' already exists.", newName));

        await _fs.MoveDirectoryAsync(project.Path, target);
        return GetProject(newName);
    }

    /// <summary>Copies an external project folder (for example one migrated from Unity) into Projects.</summary>
    public async Task<ProjectInfo> ImportAsync(IStorageFolder folder, IProgress<string>? progress = null)
    {
        string baseName = folder.Name;
        if (ValidateName(baseName) != null)
            baseName = "Imported Project";
        string name = await GetUniqueNameAsync(baseName);
        string target = VPath.Combine(ProjectsFolder, name);
        await StorageImport.CopyFolderAsync(_fs, folder, target, progress);
        return GetProject(name);
    }

    // ================= METADATA ================= //

    public async Task<string> ReadTextAsync(ProjectInfo project, string file)
    {
        string path = VPath.Combine(project.Path, file);
        return await _fs.FileExistsAsync(path) ? (await _fs.ReadAllTextAsync(path)) : string.Empty;
    }

    public Task WriteTextAsync(ProjectInfo project, string file, string text)
        => _fs.WriteAllTextAsync(VPath.Combine(project.Path, file), text);

    public async Task<string?> FindThumbnailAsync(ProjectInfo project)
    {
        foreach (var ext in ThumbnailExtensions)
        {
            string path = VPath.Combine(project.Path, "thumbnail" + ext);
            if (await _fs.FileExistsAsync(path))
                return path;
        }
        return null;
    }

    /// <summary>Replaces thumbnail.png with the picked image (other thumbnail extensions are removed).</summary>
    public async Task SetThumbnailAsync(ProjectInfo project, IStorageFile file)
    {
        byte[] data = await StorageImport.ReadAllBytesAsync(file);
        foreach (var ext in ThumbnailExtensions)
            await _fs.DeleteFileAsync(VPath.Combine(project.Path, "thumbnail" + ext));

        string extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
            extension = ".png";
        await _fs.WriteAllBytesAsync(VPath.Combine(project.Path, "thumbnail" + extension), data);
    }
}

/// <summary>Copies picked files and folders (from pickers or drag and drop) into the Vectorier file system.</summary>
public static class StorageImport
{
    public static async Task<byte[]> ReadAllBytesAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    public static async Task CopyFileAsync(IFileSystem fs, IStorageFile file, string targetPath)
    {
        await fs.WriteAllBytesAsync(targetPath, await ReadAllBytesAsync(file));
    }

    public static async Task CopyFolderAsync(IFileSystem fs, IStorageFolder folder, string targetPath, IProgress<string>? progress = null)
    {
        // Desktop: both ends are local folders, so copy them natively (in parallel).
        if (folder.TryGetLocalPath() is { } localSource && fs.GetNativePath(targetPath) is { } localTarget)
        {
            await Task.Run(() => FastCopy.CopyDirectory(localSource, localTarget, progress));
            return;
        }
        await fs.CreateDirectoryAsync(targetPath);
        await foreach (var item in folder.GetItemsAsync())
        {
            if (item is IStorageFile file)
            {
                progress?.Report(file.Name);
                await CopyFileAsync(fs, file, VPath.Combine(targetPath, file.Name));
            }
            else if (item is IStorageFolder sub)
            {
                await CopyFolderAsync(fs, sub, VPath.Combine(targetPath, sub.Name), progress);
            }
        }
    }

    /// <summary>Lists every file of a folder recursively, with its path relative to the folder.</summary>
    public static async Task<List<(IStorageFile File, string RelativeDirectory)>> ListFilesAsync(IStorageFolder folder, string relative = "")
    {
        var list = new List<(IStorageFile, string)>();
        await foreach (var item in folder.GetItemsAsync())
        {
            if (item is IStorageFile file)
                list.Add((file, relative));
            else if (item is IStorageFolder sub)
                list.AddRange(await ListFilesAsync(sub, VPath.Combine(relative, sub.Name)));
        }
        return list;
    }
}
