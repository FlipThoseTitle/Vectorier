using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    public bool CanLaunchGame => true;
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

    public static string GetGameRuntimeFolder()
    {
        if (OperatingSystem.IsWindows())
            return "win-x64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return "linux-x64";
    }

    public static string GetGameExecutableName() => OperatingSystem.IsWindows() ? "Vector-Raylib.exe" : "Vector-Raylib";

    public string? LaunchGame(IFileSystem fileSystem, string projectName, string levelName, bool hunterMode)
    {
        string folder = VPath.Combine("Game", GetGameRuntimeFolder());
        // A Mac with Apple Silicon can also run the Intel build (Rosetta), and the other way round is not possible.
        if (OperatingSystem.IsMacOS() && fileSystem.GetNativePath(VPath.Combine(folder, GetGameExecutableName())) is { } preferred && !File.Exists(preferred)
            && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            folder = VPath.Combine("Game", "osx-x64");
        string? nativeFolder = fileSystem.GetNativePath(folder);
        string? exe = fileSystem.GetNativePath(VPath.Combine(folder, GetGameExecutableName()));
        if (nativeFolder == null || exe == null || !File.Exists(exe))
            return $"The game executable was not found:\n{exe ?? folder}";

        try
        {
            // Copied from another system (zip, Windows publish), the game may have lost its "executable" permission.
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(exe);
                const UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((mode & UnixFileMode.UserExecute) == 0)
                    File.SetUnixFileMode(exe, mode | exec);
            }
            var info = new ProcessStartInfo(exe)
            {
                WorkingDirectory = nativeFolder,
                UseShellExecute = false
            };
            // The first values identify the project and level; -huntermode is optional.
            info.ArgumentList.Add("-project");
            info.ArgumentList.Add(projectName);
            info.ArgumentList.Add("-level");
            info.ArgumentList.Add(levelName);
            if (hunterMode)
                info.ArgumentList.Add("-huntermode");

            Process.Start(info);
            return null;
        }
        catch (Exception ex)
        {
            return "Failed to start the game:\n" + ex.Message;
        }
    }

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
