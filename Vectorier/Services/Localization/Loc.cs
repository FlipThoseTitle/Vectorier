using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Platform;

namespace Vectorier.Services.Localization;

/// <summary>A language of the editor, from one file in Assets/Localization (e.g. ru.json).</summary>
public sealed record LanguageInfo(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Translations of the editor's UI. The key of every text is its English wording: <c>Loc.T("Delete")</c> gives the
/// text in the current language, or the English text itself when a translation is missing. Each language is one
/// JSON file in Assets/Localization ({ "code", "name", "strings": { English: translation } }); en.json lists every
/// key and is the reference for new translations.
/// </summary>
public static class Loc
{
    public const string DefaultCode = "en";
    private const string SettingKey = "Language";
    private const string Folder = "avares://Vectorier.Core/Assets/Localization/";

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<LanguageInfo> LanguageList = new();
    private static Dictionary<string, string> _current = new();
    private static readonly List<WeakReference<IObserver<string>>> _observers = new();
    private static readonly Dictionary<WeakReference<IObserver<string>>, string> _observerKeys = new();

    /// <summary>Raised after the language changed (code-built texts can refresh themselves).</summary>
    public static event Action? LanguageChanged;

    public static IReadOnlyList<LanguageInfo> Languages
    {
        get
        {
            EnsureLoaded();
            return LanguageList;
        }
    }

    public static string CurrentCode { get; private set; } = DefaultCode;

    public static LanguageInfo CurrentLanguage => Languages.FirstOrDefault(l => l.Code == CurrentCode) ?? Languages[0];

    // ================= LOOKUP ================= //

    /// <summary>The text in the current language (the English text when there is no translation).</summary>
    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english))
            return english;
        return _current.TryGetValue(english, out var text) && !string.IsNullOrEmpty(text) ? text : english;
    }

    /// <summary>Translated format string filled with <paramref name="args"/> (<c>Loc.F("Deleted {0} elements", n)</c>).</summary>
    public static string F(string english, params object?[] args)
    {
        string format = T(english);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, english, args);
        }
    }

    // ================= LANGUAGE ================= //

    /// <summary>
    /// Start-up: the saved language, or on the very first launch (<see cref="AppState.IsFirstLaunch"/>) the
    /// computer's language when the editor has it (English otherwise). No question is asked.
    /// </summary>
    public static void Initialize()
    {
        EnsureLoaded();
        var settings = AppServices.Platform?.Settings;
        string? saved = settings?.Get(SettingKey);
        string code;
        if (AppState.IsFirstLaunch || string.IsNullOrEmpty(saved))
        {
            code = SystemLanguage();
            settings?.Set(SettingKey, code);
        }
        else
        {
            code = saved;
        }
        Apply(code);
    }

    /// <summary>The computer's UI language if the editor has it, English otherwise (regional variants count as the language).</summary>
    public static string SystemLanguage()
    {
        EnsureLoaded();
        string system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return LanguageList.Any(l => string.Equals(l.Code, system, StringComparison.OrdinalIgnoreCase)) ? system.ToLowerInvariant() : DefaultCode;
    }

    /// <summary>Preferences: switches the language and remembers it.</summary>
    public static void SetLanguage(string code)
    {
        AppServices.Platform?.Settings.Set(SettingKey, code);
        Apply(code);
    }

    private static void Apply(string code)
    {
        EnsureLoaded();
        if (!Tables.ContainsKey(code))
            code = DefaultCode;
        CurrentCode = code;
        _current = Tables.TryGetValue(code, out var table) ? table : new();
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
        }
        NotifyObservers();
        LanguageChanged?.Invoke();
    }

    // ================= FILES ================= //

    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            foreach (var uri in AssetLoader.GetAssets(new Uri(Folder), null))
            {
                if (!uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var stream = AssetLoader.Open(uri);
                Load(stream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Loc] " + ex.Message);
        }
        if (!LanguageList.Any(l => l.Code == DefaultCode))
        {
            LanguageList.Insert(0, new LanguageInfo(DefaultCode, "English"));
            Tables[DefaultCode] = new();
        }
        // English first, the others by name.
        var sorted = LanguageList.OrderBy(l => l.Code == DefaultCode ? 0 : 1).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        LanguageList.Clear();
        LanguageList.AddRange(sorted);
    }

    private static void Load(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        string code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        if (code.Length == 0)
            return;
        string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? code : code;
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("strings", out var strings) && strings.ValueKind == JsonValueKind.Object)
            foreach (var p in strings.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    table[p.Name] = p.Value.GetString() ?? "";
        Tables[code] = table;
        LanguageList.RemoveAll(l => l.Code == code);
        LanguageList.Add(new LanguageInfo(code, name));
    }

    // ================= LIVE UPDATES (XAML) ================= //

    /// <summary>A text that follows the language: emits now and again whenever the language changes.</summary>
    public static IObservable<string> Observe(string english) => new TextObservable(english);

    private static void NotifyObservers()
    {
        lock (_observers)
        {
            _observers.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var weak in _observers.ToList())
                if (weak.TryGetTarget(out var observer) && _observerKeys.TryGetValue(weak, out var key))
                    observer.OnNext(T(key));
            foreach (var dead in _observerKeys.Keys.Where(w => !w.TryGetTarget(out _)).ToList())
                _observerKeys.Remove(dead);
        }
    }

    private sealed class TextObservable : IObservable<string>
    {
        private readonly string _key;
        public TextObservable(string key) => _key = key;

        public IDisposable Subscribe(IObserver<string> observer)
        {
            // Weak: a view that goes away is not kept alive by the language list.
            var weak = new WeakReference<IObserver<string>>(observer);
            lock (_observers)
            {
                _observers.Add(weak);
                _observerKeys[weak] = _key;
            }
            observer.OnNext(T(_key));
            return new Unsubscriber(weak);
        }
    }

    private sealed class Unsubscriber : IDisposable
    {
        private WeakReference<IObserver<string>>? _weak;
        public Unsubscriber(WeakReference<IObserver<string>> weak) => _weak = weak;

        public void Dispose()
        {
            if (_weak == null)
                return;
            lock (_observers)
            {
                _observers.Remove(_weak);
                _observerKeys.Remove(_weak);
            }
            _weak = null;
        }
    }
}
