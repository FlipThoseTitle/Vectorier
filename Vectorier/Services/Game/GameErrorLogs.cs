using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Diagnostics;
using Vectorier.Services.IO;

namespace Vectorier.Services.Game;

/// <summary>
/// The game's log files, in the Vectorier folder's Editor/Logs: game-errors.log (the warnings and errors met while
/// loading or running a level) and game-crash.log (the game stopped by an unexpected error, and errors that reached
/// the application's last handler). Each report of <see cref="GameLog"/> is appended to one of them. On the desktop
/// it is written at once (a crash is written before the application ends); in the browser through the picked
/// folder, in order, in the background.
/// </summary>
public static class GameErrorLogs
{
    public const string Folder = "Editor/Logs";
    public const string ErrorFile = "game-errors.log";
    public const string CrashFile = "game-crash.log";

    /// <summary>A log is cut to its last half when it grows past this.</summary>
    private const int MaxFileLength = 2 * 1024 * 1024;

    private static readonly object NativeLock = new();
    private static readonly SemaphoreSlim BrowserLock = new(1, 1);
    private static bool _installed;

    /// <summary>The logs are written from now on, and the application's unhandled errors are logged as crashes.</summary>
    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        GameLog.Writer = Write;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                GameLog.Crash(ex, e.IsTerminating ? "Vectorier stopped (unhandled error)" : "Unhandled error");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // A background task failed and nothing awaited it: logged instead of being lost.
            GameLog.Add(GameErrorReport.From(e.Exception, "Background task", GameErrorSeverity.Error));
            e.SetObserved();
        };
    }

    /// <summary>The log file a report goes to (a path in the Vectorier folder).</summary>
    public static string PathFor(GameErrorSeverity severity) =>
        VPath.Combine(Folder, severity == GameErrorSeverity.Crash ? CrashFile : ErrorFile);

    private static void Write(GameErrorReport report)
    {
        string text = report.ToText() + Environment.NewLine;
        string path = PathFor(report.Severity);
        var fs = TryFileSystem();

        // Desktop: straight to the file (also during a crash, when nothing asynchronous would finish).
        string? native = fs?.GetNativePath(path) ?? FallbackNativePath(path);
        if (native != null)
        {
            lock (NativeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(native)!);
                Trim(native);
                File.AppendAllText(native, text, Encoding.UTF8);
            }
            return;
        }

        // Browser: through the picked folder (read, then written again with the report added).
        if (fs != null)
            _ = AppendAsync(fs, path, text);
    }

    private static IFileSystem? TryFileSystem()
    {
        try
        {
            return AppServices.FileSystem;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Before the Vectorier folder is known (desktop): the user's application data folder.</summary>
    private static string? FallbackNativePath(string path)
    {
        if (AppServices.Platform?.IsBrowser != false)
            return null;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(appData) ? null : Path.Combine(appData, "Vectorier", "Logs", VPath.GetFileName(path));
    }

    private static void Trim(string native)
    {
        var info = new FileInfo(native);
        if (!info.Exists || info.Length <= MaxFileLength)
            return;
        string all = File.ReadAllText(native);
        int cut = all.IndexOf('\n', all.Length / 2);
        File.WriteAllText(native, "(older entries removed)" + Environment.NewLine + all[(cut + 1)..], Encoding.UTF8);
    }

    private static async Task AppendAsync(IFileSystem fs, string path, string text)
    {
        await BrowserLock.WaitAsync().ConfigureAwait(false);
        try
        {
            string existing = await fs.FileExistsAsync(path) ? await fs.ReadAllTextAsync(path) : string.Empty;
            if (existing.Length > MaxFileLength)
                existing = existing[(existing.IndexOf('\n', existing.Length / 2) + 1)..];
            if (!await fs.DirectoryExistsAsync(Folder))
                await fs.CreateDirectoryAsync(Folder);
            await fs.WriteAllTextAsync(path, existing + text);
        }
        catch (Exception ex)
        {
            Console.WriteLine("The game's error log could not be written: " + ex.Message);
        }
        finally
        {
            BrowserLock.Release();
        }
    }

    /// <summary>The text of a log file (empty when there is none yet).</summary>
    public static async Task<string> ReadAsync(GameErrorSeverity severity)
    {
        string path = PathFor(severity);
        var fs = TryFileSystem();
        try
        {
            string? native = fs?.GetNativePath(path) ?? FallbackNativePath(path);
            if (native != null)
            {
                lock (NativeLock)
                    return File.Exists(native) ? File.ReadAllText(native) : string.Empty;
            }
            if (fs != null && await fs.FileExistsAsync(path))
                return await fs.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            return "The log could not be read: " + ex.Message;
        }
        return string.Empty;
    }
}
