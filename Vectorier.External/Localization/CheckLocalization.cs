// Checks one localization file against the editor's texts and writes a Markdown report.
// Usage (from the repository root): dotnet run Vectorier.External/Localization/CheckLocalization.cs -- ru
//
// en.json is the master list of every text. For another language the report lists:
//   - texts missing from that language (they show in English until translated)
//   - translations whose {0} placeholders differ from the English text
//   - entries that are no longer in en.json (text changed or removed)
// For "en" it compares en.json with the source code instead:
//   - texts used in the code (Loc.T / Loc.F / {l:Tr}) that en.json does not list yet
//   - en.json entries not found in the code (they may still be used indirectly,
//     e.g. trigger catalog names or enum values, so check before removing them)
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string code = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "";
if (code.Length == 0)
{
    Console.Error.WriteLine("Give a language code, e.g. dotnet run Vectorier.External/Localization/CheckLocalization.cs -- ru");
    return 1;
}

string root = FindRoot();
string locDir = Path.Combine(root, "Vectorier", "Assets", "Localization");
string reportDir = Path.Combine(root, "Vectorier.External", "Localization", "Reports");
Directory.CreateDirectory(reportDir);
string reportPath = Path.Combine(reportDir, $"localization-{code}.md");

var report = new StringBuilder();
report.AppendLine($"# Localization check: {code}.json");
report.AppendLine();
report.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}.");
report.AppendLine();
int problems = 0;

var (en, enErrors) = Load(Path.Combine(locDir, "en.json"));
if (en == null)
    return Fail($"en.json could not be read: {enErrors}");

if (code == "en")
{
    var used = ScanSource(Path.Combine(root, "Vectorier"));
    var notListed = used.Where(k => !en.Strings.ContainsKey(k.Key)).ToList();
    var notUsed = en.Strings.Keys.Where(k => !used.ContainsKey(k)).ToList();
    problems += Duplicates(en);
    problems += Section("Used in the code but missing from en.json", notListed.Count,
        "Add these to en.json (value = the same English text) so translators can see them.",
        notListed.Select(k => $"`{Show(k.Key)}`  \n  ↳ {k.Value}"));
    Section("In en.json but not found in the code", notUsed.Count,
        "Not necessarily wrong: trigger catalog texts, enum names and texts built at runtime are not visible to this scan. Remove an entry only if you are sure it is unused.",
        notUsed.Select(k => $"`{Show(k)}`"));
    foreach (var (k, v) in en.Strings)
        if (v.Length > 0 && v != k) { problems++; report.AppendLine($"- en.json value differs from its key: `{Show(k)}` → `{Show(v)}`"); }
}
else
{
    var (lang, errors) = Load(Path.Combine(locDir, code + ".json"));
    if (lang == null)
        return Fail($"{code}.json could not be read: {errors}");

    report.AppendLine($"Language name: **{lang.Name}** · code in file: `{lang.Code}`");
    report.AppendLine();
    if (lang.Code != code) { problems++; report.AppendLine($"> ⚠ The file is named {code}.json but its \"code\" is \"{lang.Code}\".\n"); }
    if (string.IsNullOrWhiteSpace(lang.Name)) { problems++; report.AppendLine("> ⚠ \"name\" is empty: the Preferences dropdown will show nothing for it.\n"); }

    var missing = en.Strings.Keys.Where(k => !lang.Strings.TryGetValue(k, out var v) || v.Length == 0).ToList();
    var placeholders = lang.Strings
        .Where(p => p.Value.Length > 0 && en.Strings.ContainsKey(p.Key) && Placeholders(p.Key) != Placeholders(p.Value))
        .ToList();
    var stale = lang.Strings.Keys.Where(k => !en.Strings.ContainsKey(k)).ToList();
    int translated = en.Strings.Count - missing.Count;

    report.AppendLine($"**{translated} / {en.Strings.Count}** texts translated ({(en.Strings.Count == 0 ? 100 : translated * 100 / en.Strings.Count)}%).");
    report.AppendLine();
    problems += Duplicates(lang);
    problems += Section("Placeholder mismatches", placeholders.Count,
        "The translation must contain the same {0}, {1}… as the English text, or values will be missing or the text will fail to show.",
        placeholders.Select(p => $"`{Show(p.Key)}` ({Placeholders(p.Key)})  \n  ↳ `{Show(p.Value)}` ({Placeholders(p.Value)})"));
    Section("Not translated yet (shown in English)", missing.Count,
        "Allowed: anything missing falls back to English.",
        missing.Select(k => $"`{Show(k)}`"));
    Section("Not in en.json any more", stale.Count,
        "The English text was changed or removed, so these entries are never used. Update the key to the new English text or delete the entry.",
        stale.Select(k => $"`{Show(k)}`"));
}

File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
Console.WriteLine($"Report written to {Path.GetRelativePath(root, reportPath)}");
Console.WriteLine(problems == 0 ? "No errors." : $"{problems} error(s) found, see the report.");
return problems == 0 ? 0 : 2;

// ---------------------------------------------------------------------------

int Section(string title, int count, string note, IEnumerable<string> lines)
{
    report.AppendLine($"## {title} ({count})");
    report.AppendLine();
    if (count == 0) { report.AppendLine("None."); report.AppendLine(); return 0; }
    report.AppendLine($"_{note}_");
    report.AppendLine();
    foreach (var l in lines) report.AppendLine("- " + l);
    report.AppendLine();
    return count;
}

int Duplicates(LangFile f)
{
    if (f.Duplicates.Count == 0) return 0;
    Section($"Duplicate keys in {f.FileName}", f.Duplicates.Count, "Only the last one is used; keep one.", f.Duplicates.Select(k => $"`{Show(k)}`"));
    return f.Duplicates.Count;
}

int Fail(string message)
{
    report.AppendLine("## Error");
    report.AppendLine();
    report.AppendLine(message);
    File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
    Console.Error.WriteLine(message);
    Console.WriteLine($"Report written to {Path.GetRelativePath(root, reportPath)}");
    return 2;
}

static string Show(string s) => s.Replace("\r", "").Replace("\n", "\\n").Replace("`", "'");

static string Placeholders(string s)
{
    var found = Regex.Matches(s, @"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}(?!\})").Select(m => "{" + m.Groups[1].Value + "}").Distinct().OrderBy(x => x);
    string list = string.Join(" ", found);
    return list.Length == 0 ? "none" : list;
}

static (LangFile?, string) Load(string path)
{
    if (!File.Exists(path)) return (null, "file not found");
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var r = doc.RootElement;
        var f = new LangFile
        {
            FileName = Path.GetFileName(path),
            Code = r.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
            Name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
        };
        if (r.TryGetProperty("strings", out var strings) && strings.ValueKind == JsonValueKind.Object)
            foreach (var p in strings.EnumerateObject())
            {
                if (f.Strings.ContainsKey(p.Name)) f.Duplicates.Add(p.Name);
                f.Strings[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "";
            }
        return (f, "");
    }
    catch (JsonException ex)
    {
        return (null, $"invalid JSON (line {ex.LineNumber + 1}): {ex.Message}");
    }
}

// Texts the code asks to translate, with the first place each one is used.
static Dictionary<string, string> ScanSource(string dir)
{
    var keys = new Dictionary<string, string>();
    var csCall = new Regex(@"Loc\.[TF]\(\s*""((?:[^""\\]|\\.)*)""");
    var tr = new Regex(@"\{l:Tr '((?:[^'\\]|\\.)*)'\}");
    var fmt = new Regex(@"ConverterParameter='\{\}((?:[^'\\]|\\.)*)'");
    foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
    {
        if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
        bool cs = file.EndsWith(".cs"), xaml = file.EndsWith(".axaml");
        if (!cs && !xaml) continue;
        string text = File.ReadAllText(file);
        string rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
        void Add(Match m, string key)
        {
            int line = text.Take(m.Index).Count(ch => ch == '\n') + 1;
            keys.TryAdd(key, $"{rel}:{line}");
        }
        if (cs)
            foreach (Match m in csCall.Matches(text)) Add(m, Regex.Unescape(m.Groups[1].Value.Replace("\\\"", "\"")));
        else
        {
            foreach (Match m in tr.Matches(text)) Add(m, System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Replace("\\'", "'")));
            foreach (Match m in fmt.Matches(text)) Add(m, System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Replace("\\'", "'")));
        }
    }
    return keys;
}

static string FindRoot()
{
    var d = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Vectorier", "Assets", "Localization"))) d = d.Parent;
    return d?.FullName ?? Directory.GetCurrentDirectory();
}

sealed class LangFile
{
    public string FileName = "";
    public string Code = "";
    public string Name = "";
    public Dictionary<string, string> Strings = new();
    public List<string> Duplicates = new();
}
