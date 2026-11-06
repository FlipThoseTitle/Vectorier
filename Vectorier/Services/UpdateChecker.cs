using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Vectorier.Services.Localization;
using Vectorier.ViewModels.Dialogs;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Services;

/// <summary>
/// Looks for a newer Vectorier release on GitHub (the latest published release of FlipThoseTitle/Vectorier) and
/// offers to open its page to download it. Checked once when the desktop app starts, and from Help › Check for Updates.
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "FlipThoseTitle/Vectorier";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    private const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>The release the user chose to skip (not offered again on startup).</summary>
    private const string SkippedKey = "UpdateSkippedVersion";

    private sealed record Release(Version Version, string Tag, string Name, string Url);

    /// <summary>On startup (desktop only): after the first screen is up, quietly; nothing is shown when up to date or offline.</summary>
    public static async void CheckOnStartup()
    {
        try
        {
            // The browser version is always the deployed one; a build without a version is a development build.
            if (OperatingSystem.IsBrowser() || Current() is not { } current)
                return;
            // The first launch has the tutorial's welcome to show.
            if (AppState.IsFirstLaunch)
                return;
            await Task.Delay(1500);
            var release = await FetchLatestAsync();
            if (release == null || release.Version <= current)
                return;
            if (AppServices.Platform?.Settings.Get(SkippedKey) == release.Tag)
                return;
            await OfferAsync(release, current, allowSkip: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Update check: " + ex.Message);
        }
    }

    /// <summary>Help › Check for Updates: always answers (up to date, a new version, or why it could not check).</summary>
    public static async Task CheckNowAsync()
    {
        string title = Loc.T("Check for Updates");
        Release? release;
        try
        {
            release = await FetchLatestAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(title, Loc.T("Could not reach GitHub to check for updates:\n") + ex.Message, MessageKind.Warning);
            return;
        }
        if (release == null)
        {
            await AppServices.Dialogs.ShowMessageAsync(title, Loc.T("No release was found on GitHub."), MessageKind.Info);
            return;
        }
        var current = Current();
        if (current != null && release.Version <= current)
        {
            await AppServices.Dialogs.ShowMessageAsync(title, Loc.F("You have the latest version ({0}).", EditorViewModel.AppVersion), MessageKind.Info);
            return;
        }
        await OfferAsync(release, current, allowSkip: false);
    }

    /// <summary>This build's version; null for a development build (no version given when it was published).</summary>
    private static Version? Current()
    {
        var version = Parse(EditorViewModel.AppVersion);
        return version == null || version == new Version(1, 0, 0) ? null : version;
    }

    /// <summary>"v1.11.3", "1.11.3-beta" → 1.11.3.</summary>
    private static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim().TrimStart('v', 'V');
        int end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.'))
            end++;
        text = text[..end].Trim('.');
        if (!Version.TryParse(text.Contains('.') ? text : text + ".0", out var version))
            return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    private static async Task<Release?> FetchLatestAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        if (!OperatingSystem.IsBrowser())
            request.Headers.TryAddWithoutValidation("User-Agent", "Vectorier-UpdateChecker");
        using var response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            return null;
        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (Parse(tag) is not { } version)
            return null;
        string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
        string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesUrl : ReleasesUrl;
        // Only a page of this repository is opened.
        if (!url.StartsWith("https://github.com/" + Repository + "/", StringComparison.OrdinalIgnoreCase))
            url = ReleasesUrl;
        return new Release(version, tag, string.IsNullOrWhiteSpace(name) ? tag : name, url);
    }

    private static async Task OfferAsync(Release release, Version? current, bool allowSkip)
    {
        string now = current != null ? EditorViewModel.AppVersion : Loc.T("a development build");
        var vm = new MessageDialogViewModel(Loc.T("Update Available"),
            Loc.F("A new version of Vectorier is available: {0}.\nYou have {1}.\n\nOpen the GitHub releases page to download it?", release.Name, now),
            MessageKind.Info);
        vm.Buttons.Add(new DialogButton(Loc.T("Download"), "download", isDefault: true));
        if (allowSkip)
            vm.Buttons.Add(new DialogButton(Loc.T("Skip This Version"), "skip"));
        vm.Buttons.Add(new DialogButton(Loc.T("Later"), null, isCancel: true));
        switch (await AppServices.Dialogs.ShowAsync(vm))
        {
            case "download":
                if (AppServices.Shell.CurrentTopLevel?.Launcher is { } launcher)
                    await launcher.LaunchUriAsync(new Uri(release.Url));
                break;
            case "skip":
                AppServices.Platform?.Settings.Set(SkippedKey, release.Tag);
                break;
        }
    }
}
