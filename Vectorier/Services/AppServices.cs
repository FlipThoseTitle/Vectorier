using Vectorier.Services.IO;
using Vectorier.Services.Platform;
using Vectorier.Services.Projects;

namespace Vectorier.Services;

/// <summary>
/// Application-wide services. The head projects (Desktop / Browser) set <see cref="Platform"/> before
/// Avalonia starts; the file system is set once the Vectorier folder is known.
/// </summary>
public static class AppServices
{
    public static IPlatformServices Platform { get; set; } = null!;

    public static IFileSystem FileSystem { get; private set; } = null!;

    public static ProjectService Projects { get; private set; } = null!;

    public static IShell Shell { get; set; } = null!;

    public static IDialogService Dialogs { get; set; } = null!;

    public static void SetFileSystem(IFileSystem fileSystem)
    {
        FileSystem = fileSystem;
        Projects = new ProjectService(fileSystem);
    }
}
