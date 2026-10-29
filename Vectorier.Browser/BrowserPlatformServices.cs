using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Vectorier.Services.IO;
using Vectorier.Services.Platform;

namespace Vectorier.Browser;

/// <summary>
/// Browser implementation. Local files are reached through a folder the user grants on every launch
/// (File System Access API); the game cannot be launched from a web page.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserPlatformServices : IPlatformServices
{
    public bool IsBrowser => true;
    public bool CanLaunchGame => false;
    public bool CanShowInExplorer => false;
    public ISettingsStore Settings { get; } = new MemorySettingsStore();

    public Task<IFileSystem?> TryResolveRootAsync() => Task.FromResult<IFileSystem?>(null);

    public IFileSystem? CreateFileSystemFromPickedFolder(IStorageFolder folder) => new StorageFolderFileSystem(folder);

    public void ShowInExplorer(IFileSystem fileSystem, string path) { }

    public string? LaunchGame(IFileSystem fileSystem, string projectName, string levelName, bool hunterMode)
        => "Play Level is only available in the desktop version of Vectorier.";

    public async Task PlaySoundAsync(IFileSystem fileSystem, string path, double volume = 1)
    {
        try
        {
            byte[] data = await fileSystem.ReadAllBytesAsync(path);
            string mime = VPath.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp3" => "audio/mpeg",
                ".ogg" => "audio/ogg",
                _ => "audio/wav",
            };
            // Resolves when the sound ends or is stopped.
            await PlaySoundJs(Convert.ToBase64String(data), mime, Math.Clamp(volume, 0, 1));
        }
        catch (Exception ex)
        {
            Console.WriteLine("PlaySound failed: " + ex.Message);
        }
    }

    public void SetSoundVolume(double volume)
    {
        try { SetSoundVolumeJs(Math.Clamp(volume, 0, 1)); } catch { }
    }

    public void StopSound()
    {
        try { StopSoundJs(); } catch { }
    }

    [JSImport("playSound", "vectorier")]
    private static partial Task PlaySoundJs(string base64, string mime, double volume);

    [JSImport("setSoundVolume", "vectorier")]
    private static partial void SetSoundVolumeJs(double volume);

    [JSImport("stopSound", "vectorier")]
    private static partial void StopSoundJs();
}
