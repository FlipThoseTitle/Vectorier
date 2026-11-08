using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.ViewModels.Preview;

/// <summary>Result of a preview opened with an Apply button.</summary>
public sealed record CharacterPreviewResult(int StartFrame, int EndFrame);

/// <summary>
/// Character preview: plays a .bin animation in a loop on 0.xml + skins, with start / end trimming (playback only;
/// the file is never changed). The character is the game's model: it ticks 60 times a second like the game, so its
/// cloth moves (also while paused), and the view follows its Camera node. Used by Level Settings (Models), Project
/// Manager (Animations, Gears), the Inspector (Spawn) and the Trigger Editor.
/// </summary>
public sealed partial class CharacterPreviewViewModel : DialogViewModel
{
    public const string RunAnimation = "run.bin";

    private readonly CharacterModel _character;
    private readonly List<Vector3[]> _frames;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTick;
    private double _accumulator;

    /// <summary>The view is centered this far from the model's Camera node (level units): on the character.</summary>
    public const float CameraOffsetX = -150;

    /// <summary>The game's tick and the animations' frame rate.</summary>
    private const double TickSeconds = 1.0 / 60, AnimationFps = 20;

    private CharacterPreviewViewModel(string title, CharacterModel character, List<Vector3[]> frames, int startFrame, bool showApply, string? applyText)
    {
        Title = title;
        _character = character;
        _frames = frames;
        ShowApply = showApply;
        ApplyText = applyText ?? Loc.T("Apply");
        MaxFrame = frames.Count - 1;
        _trimEnd = MaxFrame;
        _trimStart = startFrame >= 0 && startFrame <= MaxFrame ? startFrame : 0;
        _frame = _trimStart;
        // The start pose, its cloth already hanging.
        var view = _character.View;
        view.Set(_character.Pose(_frames, _trimStart), _character.AnimatedNodeCount);
        view.Settle(60);
        ComputeScale();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(TickSeconds / 2), DispatcherPriority.Render, (_, _) => Tick());
        _lastTick = _clock.Elapsed.TotalSeconds;
        _timer.Start();
        UpdatePose();
    }

    public bool ShowApply { get; }
    public string ApplyText { get; }
    public int MaxFrame { get; }

    public override bool Resizable => true;
    public override double PreferredWidth => 820;
    public override double PreferredHeight => 620;

    /// <summary>The character in level units, relative to the point the view is centered on (Y down).</summary>
    [ObservableProperty] private Geometry? _silhouette;

    /// <summary>Level units → view: a size that keeps the start pose about 60 % of the view's height.</summary>
    public double ModelHeight { get; private set; } = 200;

    [ObservableProperty] private double _frame;
    [ObservableProperty] private bool _isPlaying = true;
    [ObservableProperty] private int _trimStart;
    [ObservableProperty] private int _trimEnd;

    public string FrameText => Loc.F("Frame {0} / {1}   ·   Range {2} – {3}", (int)Math.Floor(Frame), MaxFrame, TrimStart, TrimEnd);
    public string PlayIcon => IsPlaying ? "Icon.Pause" : "Icon.Play";

    public event Action? Redraw;

    // The model takes the new frame at its next tick (scrubbing moves it like the game would).
    partial void OnFrameChanged(double value)
    {
        OnPropertyChanged(nameof(FrameText));
        UpdatePose();
    }

    partial void OnIsPlayingChanged(bool value)
    {
        _lastTick = _clock.Elapsed.TotalSeconds;
        OnPropertyChanged(nameof(PlayIcon));
    }

    partial void OnTrimStartChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, MaxFrame);
        if (clamped != value) { TrimStart = clamped; return; }
        if (TrimEnd < value) TrimEnd = value;
        if (Frame < value || Frame > TrimEnd) Frame = value;
        OnPropertyChanged(nameof(FrameText));
    }

    partial void OnTrimEndChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, MaxFrame);
        if (clamped != value) { TrimEnd = clamped; return; }
        if (TrimStart > value) TrimStart = value;
        if (Frame > value || Frame < TrimStart) Frame = TrimStart;
        OnPropertyChanged(nameof(FrameText));
    }

    /// <summary>Game ticks due since the last time: the frame advances (when playing), then the model ticks.</summary>
    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        _accumulator += Math.Min(now - _lastTick, 0.25);
        _lastTick = now;
        if (_accumulator < TickSeconds)
            return;
        double frame = Frame;
        while (_accumulator >= TickSeconds)
        {
            _accumulator -= TickSeconds;
            if (IsPlaying)
                frame = Advance(frame);
            _character.View.Tick(_character.Pose(_frames, frame), _character.AnimatedNodeCount);
        }
        if (Math.Abs(frame - Frame) > 1e-9)
            Frame = frame; // redraws
        else
            UpdatePose();
    }

    /// <summary>One tick of the 20 fps animation; the trimmed range loops (a single frame stays still).</summary>
    private double Advance(double frame)
    {
        if (TrimEnd <= TrimStart)
            return TrimStart;
        double next = frame + TickSeconds * AnimationFps;
        double length = TrimEnd - TrimStart;
        if (next > TrimEnd)
            next = TrimStart + (next - TrimEnd) % length;
        return next;
    }

    /// <summary>The point the view is centered on: the Camera node, offset onto the character.</summary>
    private Vector3 Origin()
    {
        var view = _character.View;
        var camera = view.Node("Camera") ?? view.Node("NPivot") ?? Vector3.Zero;
        return camera + new Vector3(CameraOffsetX, 0, 0);
    }

    private void ComputeScale()
    {
        _character.View.Draw(Origin(), out var bounds);
        ModelHeight = Math.Max(50, bounds.Height);
    }

    private void UpdatePose()
    {
        Silhouette = _character.View.Draw(Origin(), out _);
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void TogglePlay() => IsPlaying = !IsPlaying;

    [RelayCommand]
    private void Restart()
    {
        Frame = TrimStart;
        IsPlaying = true;
    }

    [RelayCommand]
    private void ResetTrim()
    {
        TrimStart = 0;
        TrimEnd = MaxFrame;
    }

    [RelayCommand]
    private void ApplyPreview()
    {
        _timer.Stop();
        Close(new CharacterPreviewResult(TrimStart, TrimEnd));
    }

    [RelayCommand]
    private void ClosePreview()
    {
        _timer.Stop();
        Close(null);
    }

    public override void Cancel()
    {
        _timer.Stop();
        base.Cancel();
    }

    // ================= OPENING ================= //

    /// <summary>
    /// Loads 0.xml + skins and the animation, then shows the preview. Problems (conflicts, too few nodes...) are
    /// reported and nothing opens. Returns the trim when Apply was pressed.
    /// </summary>
    public static async Task<CharacterPreviewResult?> ShowAsync(ProjectInfo project, string title, IEnumerable<string> skins, string animationPath,
        int startFrame = 0, bool showApply = false, string? applyText = null)
    {
        CharacterPreviewViewModel vm;
        try
        {
            var character = await CharacterModel.LoadAsync(project, skins);
            if (!await AppServices.FileSystem.FileExistsAsync(animationPath))
                throw new CharacterPreviewException(Loc.F("Animation not found: {0}", VPath.GetFileName(animationPath)));
            var frames = character.ReadAnimation(await AppServices.FileSystem.ReadAllBytesAsync(animationPath));
            vm = new CharacterPreviewViewModel(title, character, frames, startFrame, showApply, applyText);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Character Preview"), ex.Message, MessageKind.Error);
            return null;
        }
        return await AppServices.Dialogs.ShowAsync(vm) as CharacterPreviewResult;
    }

    public static string AnimationPath(ProjectInfo project, string fileName) => VPath.Combine(project.AnimationsFolder, "data", fileName);
}
