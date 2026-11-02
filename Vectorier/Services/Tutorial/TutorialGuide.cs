using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vectorier.Services.Localization;
using Vectorier.ViewModels;
using Vectorier.ViewModels.Dialogs;
using Vectorier.ViewModels.Editor;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.Services.Tutorial;

/// <summary>One prompt of the tutorial.</summary>
public sealed class TutorialStep
{
    public string Title = string.Empty;
    public string Text = string.Empty;

    /// <summary>Text built when the step starts (names chosen by the user...), instead of <see cref="Text"/>.</summary>
    public Func<string>? DynamicText;

    /// <summary>The places that stay lit (and, when <see cref="Interactive"/>, can be used).</summary>
    public Func<IEnumerable<Control?>>? Targets;

    /// <summary>The user can click and drag inside the lit places.</summary>
    public bool Interactive;

    /// <summary>When set, decides <see cref="Interactive"/> on every tick (e.g. clickable only until something is open).</summary>
    public Func<bool>? InteractiveWhen;

    /// <summary>When set, Next is only shown while it returns true (e.g. once the right tab is open).</summary>
    public Func<bool>? NextWhen;

    /// <summary>Where to place things in the Scene (dashed boxes, ringed crosses), re-read on every tick.</summary>
    public Func<IReadOnlyList<SceneGuide>>? Guides;

    /// <summary>The card sits in the window's top-left corner instead of next to the lit places (it would cover them).</summary>
    public bool CardTopLeft;

    /// <summary>The part of the tutorial this step belongs to (shown with the step's number in that part).</summary>
    public string Section = string.Empty;

    /// <summary>False: the window is not dimmed and stays fully usable (only the card is shown).</summary>
    public bool Dimmed = true;

    /// <summary>The step ends by itself when this becomes true; null: the user clicks Next.</summary>
    public Func<bool>? Done;

    /// <summary>Show Next even though the step has a <see cref="Done"/> condition (it ends either way).</summary>
    public bool AllowNext;

    public string? NextText;

    /// <summary>The step's targets are in the open dialog (Level Settings...) rather than the main window.</summary>
    public bool InDialog;

    /// <summary>Not applicable here (e.g. playing the game in the browser): skipped.</summary>
    public Func<bool>? Skip;

    public Action? Enter;
    public Action? Leave;

    /// <summary>Called about ten times a second while the step is shown (keep a selection, clamp a size...).</summary>
    public Action? Tick;
}

/// <summary>
/// The guided tutorial: a sequence of prompts over the app's windows. Each prompt dims the window except the places
/// the user should look at or use, and waits for the user to do what it asks (or to click Next). Offered on the
/// first launch and from Help › Tutorial; it can be left at any time with Exit Tutorial.
/// </summary>
public static partial class TutorialGuide
{
    private const string DoneSettingKey = "Tutorial.Done";

    private static List<TutorialStep> _steps = new();
    private static int _index = -1;
    private static DispatcherTimer? _timer;
    private static TutorialOverlay? _overlay;
    private static OverlayLayer? _layer;
    private static List<Control?> _targets = new();
    private static int _tickCount;
    private static bool _exiting;

    public static bool IsActive => _index >= 0;

    // ================= START / STOP ================= //

    /// <summary>First launch: the welcome window over Project Selection.</summary>
    public static async void OfferOnFirstLaunch()
    {
        if (!AppState.IsFirstLaunch || AppServices.Platform?.Settings.Get(DoneSettingKey) == "1")
            return;
        // Let Project Selection appear first.
        await Task.Delay(400);
        await WelcomeAsync();
    }

    /// <summary>Help › Tutorial: the welcome window, then back to Project Selection to start from the beginning.</summary>
    public static async Task RestartFromHelpAsync()
    {
        if (IsActive)
            return;
        var dialog = new TutorialWelcomeViewModel();
        if (await AppServices.Dialogs.ShowAsync(dialog) is not true)
            return;
        var screen = AppServices.Shell.CurrentScreen;
        if (screen is not ProjectSelectionViewModel)
        {
            if (screen != null && !await screen.CanCloseAsync())
                return;
            AppServices.Shell.Show(new ProjectSelectionViewModel());
        }
        Start();
    }

    private static async Task WelcomeAsync()
    {
        var dialog = new TutorialWelcomeViewModel();
        var result = await AppServices.Dialogs.ShowAsync(dialog);
        if (result is true)
            Start();
        else
            AppServices.Platform?.Settings.Set(DoneSettingKey, "1");
    }

    public static void Start()
    {
        if (IsActive)
            return;
        _steps = BuildSteps();
        _index = -1;
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
        Advance();
    }

    public static void Stop(bool completed)
    {
        if (!IsActive)
            return;
        try
        {
            Current?.Leave?.Invoke();
        }
        catch
        {
            // leaving must never fail
        }
        _index = -1;
        _timer?.Stop();
        ClearFilters();
        SceneGuides.Clear();
        Detach();
        AppServices.Platform?.Settings.Set(DoneSettingKey, "1");
        _ = completed;
    }

    private static TutorialStep? Current => _index >= 0 && _index < _steps.Count ? _steps[_index] : null;

    private static void Advance()
    {
        Current?.Leave?.Invoke();
        while (true)
        {
            _index++;
            if (_index >= _steps.Count)
            {
                Stop(true);
                return;
            }
            var step = _steps[_index];
            if (step.Skip?.Invoke() == true)
                continue;
            try
            {
                step.Enter?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Tutorial] " + ex);
            }
            _targets.Clear();
            _tickCount = 0;
            _shownText = step.DynamicText?.Invoke() ?? Loc.T(step.Text);
            Tick();
            return;
        }
    }

    private static string _shownText = string.Empty;

    private static async void OnExitClicked()
    {
        if (_exiting)
            return;
        _exiting = true;
        try
        {
            if (await AppServices.Dialogs.ConfirmAsync(Loc.T("Exit Tutorial"),
                    Loc.T("Leave the tutorial? You can start it again any time from Help › Tutorial in the editor."),
                    Loc.T("Exit Tutorial"), Loc.T("Continue Tutorial")))
                Stop(false);
        }
        finally
        {
            _exiting = false;
        }
    }

    // ================= TICK ================= //

    private static void Tick()
    {
        var step = Current;
        if (step == null)
            return;
        try
        {
            step.Tick?.Invoke();
            SceneGuides.Set(step.Guides?.Invoke() ?? Array.Empty<SceneGuide>());
            if (step.Done?.Invoke() == true)
            {
                Advance();
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Tutorial] " + ex);
        }

        var host = step.InDialog ? DialogHost() ?? MainHost() : MainHost();
        if (host == null)
        {
            Detach();
            return;
        }
        if (!Attach(host))
            return;

        // The game is running: the editor waits until it is closed.
        if (AppServices.Platform?.IsGameRunning == true)
        {
            _overlay!.ShowBusy(Loc.T("The game is running.\nClose the game window to continue the tutorial."));
            return;
        }

        // Looking for the targets walks the window's controls: not on every tick.
        if (_tickCount++ % 3 == 0 || _targets.Any(t => t == null || !t.IsAttachedToVisualTree()))
            _targets = step.Targets?.Invoke().ToList() ?? new List<Control?>();

        var holes = new List<Rect>();
        foreach (var target in _targets)
        {
            if (target == null || !target.IsEffectivelyVisible || !target.IsAttachedToVisualTree() || !ReferenceEquals(TopLevel.GetTopLevel(target), host))
                continue;
            var origin = target.TranslatePoint(new Point(0, 0), _overlay!);
            if (origin == null)
                continue;
            var rect = new Rect(origin.Value, target.Bounds.Size).Intersect(new Rect(_overlay!.Bounds.Size));
            if (rect.Width > 0 && rect.Height > 0)
                holes.Add(rect);
        }

        bool showNext = (step.Done == null || step.AllowNext) && (step.NextWhen?.Invoke() ?? true);
        string? nextText = showNext ? Loc.T(step.NextText ?? "Next") : null;
        if (step.DynamicText != null)
            _shownText = step.DynamicText();
        bool interactive = step.InteractiveWhen?.Invoke() ?? step.Interactive;

        // Counted per section (a handful of steps each) rather than over the whole tutorial.
        var shown = _steps.Where(s => s.Skip?.Invoke() != true).ToList();
        var sections = shown.Select(s => s.Section).Distinct().ToList();
        var inSection = shown.Where(s => s.Section == step.Section).ToList();
        int stepNumber = inSection.IndexOf(step) + 1;
        string sectionText = Loc.F("Part {0} of {1}: {2}", sections.IndexOf(step.Section) + 1, sections.Count, Loc.T(step.Section));
        _overlay!.CardTopLeft = step.CardTopLeft;
        _overlay!.Show(sectionText, Loc.T(step.Title), _shownText, nextText, Loc.T("Exit Tutorial"), $"{stepNumber} / {inSection.Count}", holes, interactive, step.Dimmed);
    }

    private static TopLevel? MainHost() => AppServices.Shell?.CurrentTopLevel;

    /// <summary>The newest open dialog window (desktop); in the browser dialogs live in the main view.</summary>
    private static TopLevel? DialogHost()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.Windows.OfType<DialogWindow>().LastOrDefault(w => w.IsVisible);
        return null;
    }

    private static bool Attach(TopLevel host)
    {
        var layer = OverlayLayer.GetOverlayLayer(host);
        if (layer == null)
            return false;
        if (_overlay == null)
        {
            _overlay = new TutorialOverlay();
            _overlay.NextClicked += () => Advance();
            _overlay.ExitClicked += OnExitClicked;
        }
        if (!ReferenceEquals(layer, _layer))
        {
            Detach();
            _layer = layer;
            layer.Children.Add(_overlay);
            _targets.Clear();
        }
        else if (!layer.Children.Contains(_overlay))
        {
            layer.Children.Add(_overlay);
        }
        var size = layer.Bounds.Size;
        if (_overlay.Width != size.Width || _overlay.Height != size.Height)
        {
            _overlay.Width = size.Width;
            _overlay.Height = size.Height;
        }
        return true;
    }

    private static void Detach()
    {
        if (_overlay != null && _layer != null)
            _layer.Children.Remove(_overlay);
        _layer = null;
    }

    // ================= FINDING CONTROLS ================= //

    private static TopLevel? Root(bool dialog = false) => dialog ? DialogHost() ?? MainHost() : MainHost();

    private static IEnumerable<T> All<T>(Visual? root) where T : Visual =>
        root == null ? Enumerable.Empty<T>() : root.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible);

    private static T? Find<T>(Func<T, bool>? predicate = null, bool dialog = false) where T : Visual =>
        All<T>(Root(dialog)).FirstOrDefault(c => predicate == null || predicate(c));

    /// <summary>The button running a command (optionally with a style class: "accent" for the main buttons).</summary>
    private static Control? ByCommand(ICommand? command, string? cssClass = null, bool dialog = false) =>
        command == null ? null : Find<Button>(b => ReferenceEquals(b.Command, command) && (cssClass == null || b.Classes.Contains(cssClass)), dialog);

    /// <summary>A control showing a text (e.g. a tab or a button with an icon and a label).</summary>
    private static T? WithText<T>(string englishText, bool dialog = false) where T : Control
    {
        string text = Loc.T(englishText);
        return Find<T>(c => c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text), dialog);
    }

    private static Control? Named(string name, bool dialog = false) => Find<Control>(c => c.Name == name, dialog);

    private static Control? ItemOf(object? data, bool dialog = false) =>
        data == null ? null : Find<ListBoxItem>(i => ReferenceEquals(i.DataContext, data), dialog);

    private static Control? AncestorPanel(Control? control) =>
        control?.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("panel")) ?? control;

    private static IEnumerable<Control?> Many(params Control?[] controls) => controls;
}
