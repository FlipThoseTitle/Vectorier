using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.Assets;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Projects;
using Vectorier_Editor_Avalonia.ViewModels.Dialogs;

namespace Vectorier_Editor_Avalonia.ViewModels.Preview;

public enum PreviewCamera
{
    /// <summary>Centered on NPivot once; the camera does not move afterwards.</summary>
    FixedOnPivot,
    /// <summary>Follows the "Camera" node every frame.</summary>
    FollowCameraNode,
}

/// <summary>Result of a preview opened with an Apply button.</summary>
public sealed record CharacterPreviewResult(int StartFrame, int EndFrame);

/// <summary>
/// Character preview: plays a .bin animation in a loop on 0.xml + skins, with start / end trimming (playback only;
/// the file is never changed). Used by Level Settings (Models), Project Manager (Animations) and the Trigger Editor.
/// </summary>
public sealed partial class CharacterPreviewViewModel : DialogViewModel
{
    public const string RunAnimation = "run.bin";

    private readonly CharacterModel _character;
    private readonly List<Vector3[]> _frames;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTick;
    private Vector3 _fixedOrigin;

    private CharacterPreviewViewModel(string title, CharacterModel character, List<Vector3[]> frames, PreviewCamera camera, int startFrame, bool showApply, string? applyText)
    {
        Title = title;
        _character = character;
        _frames = frames;
        Camera = camera;
        ShowApply = showApply;
        ApplyText = applyText ?? "Apply";
        MaxFrame = frames.Count - 1;
        _trimEnd = MaxFrame;
        _trimStart = startFrame >= 0 && startFrame <= MaxFrame ? startFrame : 0;
        _frame = _trimStart;
        _fixedOrigin = OriginFor(_character.Pose(_frames, _trimStart));
        ComputeScale();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 60), DispatcherPriority.Render, (_, _) => Tick());
        _lastTick = _clock.Elapsed.TotalSeconds;
        _timer.Start();
        UpdatePose();
    }

    public PreviewCamera Camera { get; }
    public bool ShowApply { get; }
    public string ApplyText { get; }
    public int MaxFrame { get; }

    public override bool Resizable => true;
    public override double PreferredWidth => 820;
    public override double PreferredHeight => 620;

    /// <summary>Silhouette in model units, relative to the camera point (Y down).</summary>
    [ObservableProperty] private Geometry? _silhouette;

    /// <summary>Model units → view: a size that keeps the whole base pose about 60 % of the view's height.</summary>
    public double ModelHeight { get; private set; } = 200;

    [ObservableProperty] private double _frame;
    [ObservableProperty] private bool _isPlaying = true;
    [ObservableProperty] private int _trimStart;
    [ObservableProperty] private int _trimEnd;

    public string FrameText => $"Frame {(int)Math.Floor(Frame)} / {MaxFrame}   ·   Range {TrimStart} – {TrimEnd}";
    public string PlayIcon => IsPlaying ? "Icon.Pause" : "Icon.Play";

    public event Action? Redraw;

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

    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastTick;
        _lastTick = now;
        if (!IsPlaying)
            return;
        if (TrimEnd <= TrimStart)
        {
            // A single frame: the static pose.
            if (Math.Abs(Frame - TrimStart) > 1e-9) Frame = TrimStart;
            return;
        }
        // The source runs at 20 fps; the range loops.
        double next = Frame + Math.Min(dt, 0.25) * MoveVisualizerFps;
        double length = TrimEnd - TrimStart;
        if (next > TrimEnd)
            next = TrimStart + (next - TrimEnd) % length;
        Frame = next;
    }

    private const double MoveVisualizerFps = 20;

    private Vector3 OriginFor(Vector3[] pose) => Camera == PreviewCamera.FollowCameraNode
        ? _character.NodePosition(pose, "Camera") ?? _character.NodePosition(pose, "NPivot") ?? Vector3.Zero
        : _character.NodePosition(pose, "NPivot") ?? Vector3.Zero;

    private void ComputeScale()
    {
        var pose = _character.Pose(_frames, TrimStart);
        _character.Model.BuildSilhouette(pose, OriginFor(pose), out var bounds);
        ModelHeight = Math.Max(50, bounds.Height);
    }

    private void UpdatePose()
    {
        var pose = _character.Pose(_frames, Frame);
        var origin = Camera == PreviewCamera.FollowCameraNode ? OriginFor(pose) : _fixedOrigin;
        Silhouette = _character.Model.BuildSilhouette(pose, origin, out _);
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
        PreviewCamera camera, int startFrame = 0, bool showApply = false, string? applyText = null)
    {
        CharacterPreviewViewModel vm;
        try
        {
            var character = await CharacterModel.LoadAsync(project, skins);
            if (!await AppServices.FileSystem.FileExistsAsync(animationPath))
                throw new CharacterPreviewException($"Animation not found: {VPath.GetFileName(animationPath)}");
            var frames = character.ReadAnimation(await AppServices.FileSystem.ReadAllBytesAsync(animationPath));
            vm = new CharacterPreviewViewModel(title, character, frames, camera, startFrame, showApply, applyText);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Character Preview", ex.Message, MessageKind.Error);
            return null;
        }
        return await AppServices.Dialogs.ShowAsync(vm) as CharacterPreviewResult;
    }

    public static string AnimationPath(ProjectInfo project, string fileName) => VPath.Combine(project.AnimationsFolder, "data", fileName);
}
