#nullable enable
using System;
using System.Collections.Generic;
using System.Xml;

namespace Vectorier.Gameplay.Diagnostics;

/// <summary>
/// The game's error log: every problem met while loading or running (warnings and errors), and, kept apart, the
/// crashes (errors nothing expected). The reports of this session are kept for the editor's Game Error Log
/// window; each is also handed to <see cref="Writer"/>, which the editor sets to append it to its log files.
/// </summary>
public static class GameLog
{
    private const int MaxKept = 500;

    private static readonly List<GameErrorReport> _reports = new();

    /// <summary>Called for every report (from any thread). The editor writes the log files with it.</summary>
    public static Action<GameErrorReport>? Writer { get; set; }

    /// <summary>A report was added, or the reports cleared (from any thread).</summary>
    public static event Action? Changed;

    /// <summary>This session's reports, oldest first.</summary>
    public static IReadOnlyList<GameErrorReport> Reports
    {
        get
        {
            lock (_reports)
                return _reports.ToArray();
        }
    }

    /// <summary>The warnings logged since <see cref="BeginCount"/> (a level's loading problems).</summary>
    public static int WarningsSinceMark
    {
        get
        {
            lock (_reports)
                return _warnings - _warningsMark;
        }
    }

    private static int _warnings;
    private static int _warningsMark;

    public static void BeginCount()
    {
        lock (_reports)
            _warningsMark = _warnings;
    }

    public static GameErrorReport Error(Exception exception, string stage) =>
        Add(GameErrorReport.From(exception, stage, GameErrorSeverity.Error));

    public static GameErrorReport Crash(Exception exception, string stage) =>
        Add(GameErrorReport.From(exception, stage, GameErrorSeverity.Crash));

    /// <summary>Something was wrong but the game went on.</summary>
    public static GameErrorReport Warning(string stage, string problem, string? file = null, XmlNode? node = null, string? attribute = null) =>
        Add(GameErrorReport.Warning(stage, problem, file, node, attribute));

    public static GameErrorReport Add(GameErrorReport report)
    {
        lock (_reports)
        {
            // The same warning each frame (a missing object drawn again) is logged once.
            if (report.Severity == GameErrorSeverity.Warning && _seenWarnings.Contains(report.Summary))
                return report;
            if (report.Severity == GameErrorSeverity.Warning)
            {
                _seenWarnings.Add(report.Summary);
                _warnings++;
            }
            _reports.Add(report);
            if (_reports.Count > MaxKept)
                _reports.RemoveAt(0);
        }
        Console.WriteLine(report.ToText(withDetails: report.Severity != GameErrorSeverity.Warning));
        try
        {
            Writer?.Invoke(report);
        }
        catch (Exception ex)
        {
            Console.WriteLine("The game's error log could not be written: " + ex.Message);
        }
        Changed?.Invoke();
        return report;
    }

    private static readonly HashSet<string> _seenWarnings = new();

    /// <summary>A new level: its warnings are logged again if they happen again.</summary>
    public static void ForgetWarnings()
    {
        lock (_reports)
            _seenWarnings.Clear();
    }

    /// <summary>The Game Error Log's Clear: this session's reports are forgotten (the files keep them).</summary>
    public static void Clear()
    {
        lock (_reports)
        {
            _reports.Clear();
            _seenWarnings.Clear();
            _warnings = _warningsMark = 0;
        }
        Changed?.Invoke();
    }
}
