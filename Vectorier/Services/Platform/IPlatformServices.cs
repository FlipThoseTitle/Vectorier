using System.Threading.Tasks;
using Vectorier.Services.IO;

namespace Vectorier.Services.Platform;

/// <summary>
/// Operations that genuinely differ between the desktop and browser builds.
/// Everything else in the editor is shared.
/// </summary>
public interface IPlatformServices
{
    bool IsBrowser { get; }

    /// <summary>Desktop only: folders can be revealed in the OS file manager.</summary>
    bool CanShowInExplorer { get; }

    /// <summary>Settings persisted between sessions.</summary>
    ISettingsStore Settings { get; }

    /// <summary>
    /// Finds the Vectorier application folder automatically. Returns null when the user has to pick it
    /// (always the case in the browser, where local folders need an explicit user grant).
    /// </summary>
    Task<IFileSystem?> TryResolveRootAsync();

    /// <summary>Creates a file system for a folder the user picked.</summary>
    IFileSystem? CreateFileSystemFromPickedFolder(Avalonia.Platform.Storage.IStorageFolder folder);

    void ShowInExplorer(IFileSystem fileSystem, string path);

    /// <summary>
    /// The system's audio output for the game played in the editor (the game mixer pulls from it). Null when the
    /// system has none: the game is then silent.
    /// </summary>
    Gameplay.Audio.IAudioOutput? CreateAudioOutput();

    /// <summary>
    /// Plays a sound preview at a volume from 0 to 1. The task completes when the sound ends or is stopped.
    /// </summary>
    Task PlaySoundAsync(IFileSystem fileSystem, string path, double volume = 1);

    /// <summary>Changes the volume of the sound that is playing (where the platform allows it).</summary>
    void SetSoundVolume(double volume);

    void StopSound();
}

public interface ISettingsStore
{
    string? Get(string key);
    void Set(string key, string? value);
}
