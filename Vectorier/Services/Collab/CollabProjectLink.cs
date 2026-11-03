using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.Services.Collab;

/// <summary>
/// Which project is which. A hosted project gets a random key (.vectorier/collab-host.json); a participant's copy
/// remembers the key of the project it mirrors (.vectorier/collab-link.json). So joining the same host again reuses
/// that copy, and a different project that only has the same name is never touched.
/// </summary>
public static class CollabProjectLink
{
    private sealed record KeyFile(string Key, string? Name = null);

    private static string HostFile(string root) => Path.Combine(root, CollabProjectFiles.CacheFolder, "collab-host.json");
    private static string LinkFile(string root) => Path.Combine(root, CollabProjectFiles.CacheFolder, "collab-link.json");

    /// <summary>The hosted project's key (made the first time it is shared).</summary>
    public static string HostKey(string root)
    {
        if (Read(HostFile(root)) is { } existing)
            return existing.Key;
        // A project that is itself someone's shared copy keeps that identity when it is hosted.
        if (Read(LinkFile(root)) is { } linked)
            return linked.Key;
        string key = Guid.NewGuid().ToString("N");
        Write(HostFile(root), new KeyFile(key));
        return key;
    }

    private static KeyFile? Read(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(file)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Write(string file, KeyFile value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(value));
    }

    /// <summary>The outcome of looking for the local copy of a host's project.</summary>
    public sealed record Resolution(ProjectInfo? Project, string? Root, bool Created, string? Problem);

    /// <summary>
    /// Finds this computer's copy of the host's project, or creates it from the local project template (only what
    /// differs is downloaded afterwards). Refuses when an unrelated project already has that name.
    /// </summary>
    public static async Task<Resolution> ResolveAsync(string projectName, string key)
    {
        if (ProjectService.ValidateName(projectName) != null || string.Equals(projectName, ProjectService.TemplateName, StringComparison.OrdinalIgnoreCase))
            return new Resolution(null, null, false, Loc.F("The host's project name \"{0}\" cannot be used here.", projectName));
        var fs = AppServices.FileSystem;
        var service = new ProjectService(fs);
        var project = service.GetProject(projectName);
        bool exists = await fs.DirectoryExistsAsync(project.Path);
        string? root = fs.GetNativePath(project.Path);
        if (root == null)
            return new Resolution(null, null, false, Loc.T("Live Collaboration needs the Projects folder on this computer."));

        if (exists)
        {
            bool same = Read(LinkFile(root))?.Key == key || Read(HostFile(root))?.Key == key;
            if (!same)
                return new Resolution(null, null, false, Loc.F(
                    "A project named \"{0}\" already exists on this computer and is not a copy of the host's project.\n\nRename your project in Project Selection, then join again. Nothing was downloaded or changed.", projectName));
            return new Resolution(project, root, false, null);
        }

        // New copy: the local template first (most files are usually the same), then only the differences are downloaded.
        string template = Path.Combine(fs.GetNativePath(ProjectService.ProjectsFolder) ?? string.Empty, ProjectService.TemplateName);
        if (Directory.Exists(template))
            await fs.CopyDirectoryAsync(Vectorier.Services.IO.VPath.Combine(ProjectService.ProjectsFolder, ProjectService.TemplateName), project.Path);
        else
            Directory.CreateDirectory(root);
        // Not the template's identity: this copy belongs to the host's project.
        try { File.Delete(HostFile(root)); } catch { }
        Write(LinkFile(root), new KeyFile(key, projectName));
        return new Resolution(project, root, true, null);
    }
}
