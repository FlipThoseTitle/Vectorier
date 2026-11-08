using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Vectorier.Services.IO;

namespace Vectorier.Services.Platform;

/// <summary>
/// Windows / Linux / macOS implementation. Lives in the shared project because it only needs
/// System.IO and System.Diagnostics; the desktop head simply instantiates it.
/// </summary>
public sealed class DesktopPlatformServices : IPlatformServices
{
    public const string RootSettingKey = "LastRoot";

    private readonly string[] _args;

    public DesktopPlatformServices(string[] args)
    {
        _args = args;
        Settings = new JsonSettingsStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vectorier", "settings.json"));
    }

    public bool IsBrowser => false;
    public bool CanShowInExplorer => true;
    public ISettingsStore Settings { get; }

    public Task<IFileSystem?> TryResolveRootAsync()
    {
        string? root = FindRoot();
        return Task.FromResult<IFileSystem?>(root == null ? null : new NativeFileSystem(root));
    }

    private string? FindRoot()
    {
        // 1. Explicit command line argument: --root "path"
        for (int i = 0; i < _args.Length - 1; i++)
        {
            if (string.Equals(_args[i], "--root", StringComparison.OrdinalIgnoreCase) && LooksLikeRoot(_args[i + 1]))
                return _args[i + 1];
        }

        // 2. Environment variable
        string? env = Environment.GetEnvironmentVariable("VECTORIER_ROOT");
        if (!string.IsNullOrEmpty(env) && LooksLikeRoot(env))
            return env;

        // 3. Folder that contains the executable, or one of its parents (development builds live in bin/...).
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; dir != null && depth < 8; depth++)
        {
            if (LooksLikeRoot(dir))
                return dir;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        // 4. Folder picked previously
        string? last = Settings.Get(RootSettingKey);
        if (!string.IsNullOrEmpty(last) && LooksLikeRoot(last))
            return last;

        return null;
    }

    public static bool LooksLikeRoot(string path)
    {
        try
        {
            return Directory.Exists(Path.Combine(path, "Projects")) && Directory.Exists(Path.Combine(path, "Editor"));
        }
        catch
        {
            return false;
        }
    }

    public IFileSystem? CreateFileSystemFromPickedFolder(IStorageFolder folder)
    {
        string? path = folder.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return null;
        Settings.Set(RootSettingKey, path);
        return new NativeFileSystem(path);
    }

    public void ShowInExplorer(IFileSystem fileSystem, string path)
    {
        string? native = fileSystem.GetNativePath(path);
        if (native == null)
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(native))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{native}\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{native}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", File.Exists(native) ? new[] { "-R", native } : new[] { native });
            }
            else
            {
                Process.Start("xdg-open", File.Exists(native) ? Path.GetDirectoryName(native)! : native);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ShowInExplorer failed: " + ex.Message);
        }
    }

    // ================= GAME AUDIO ================= //

    /// <summary>The system's own audio: waveOut (Windows), PulseAudio or ALSA (Linux), Core Audio (macOS).</summary>
    public Gameplay.Audio.IAudioOutput? CreateAudioOutput() => Audio.DesktopAudio.Create();

    // ================= SOUND PREVIEW ================= //

    private readonly SoundPreview _sound = new();

    public Task PlaySoundAsync(IFileSystem fileSystem, string path, double volume = 1)
    {
        string? native = fileSystem.GetNativePath(path);
        if (native == null || !File.Exists(native))
            return Task.CompletedTask;
        return _sound.PlayAsync(native, volume);
    }

    public void SetSoundVolume(double volume) => _sound.SetVolume(volume);

    public void StopSound() => _sound.Stop();
}
