using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

/// <summary>
/// The Scene view: draws the level with parallax and handles selection, the transform tools, placement,
/// Blender-style G/R/S, Alt-drag duplication and navigation (right drag pans, Ctrl + right drag zooms).
/// </summary>
public sealed partial class SceneView : Control, IInternalDropTarget
{
    public static readonly StyledProperty<EditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<SceneView, EditorViewModel?>(nameof(Editor));

    private static readonly IBrush Background = Brushes.White;
    private static readonly Pen RubberBandPen = new(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7)), 1);
    private static readonly IBrush RubberBandFill = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x78, 0xD7));
    private static readonly Pen HandlePen = new(new SolidColorBrush(Color.FromRgb(0x00, 0xA0, 0x30)), 1);
    private static readonly IBrush HandleFill = Brushes.White;
    private static readonly Pen PlacementPen = new(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7)), 1, new DashStyle(new double[] { 4, 3 }, 0));

    private EditorViewModel? _editor;
    private SceneRenderer? _renderer;
    private Point _center;
    private double _zoom = 0.5;
    private bool _animating;
    private bool _framedOnce;

    static SceneView()
    {
        FocusableProperty.OverrideDefaultValue<SceneView>(true);
        ClipToBoundsProperty.OverrideDefaultValue<SceneView>(true);
    }

    public SceneView()
    {
        TextureLoadedHandler = _ => InvalidateVisual();
        DoubleTapped += OnSceneDoubleTapped;
    }

    public EditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public SceneRenderer? Renderer => _renderer;

    public double Zoom => _zoom;

    public SceneViewport Viewport => new(_center, _zoom, Bounds.Size);

    private Action<string> TextureLoadedHandler { get; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
            Attach(change.GetNewValue<EditorViewModel?>());
        else if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            // Changes made while hidden were skipped.
            InvalidateVisual();
        }
        else if (change.Property == BoundsProperty)
        {
            UpdateEditorCenter();
            if (!_framedOnce && Bounds.Width > 0)
                FrameAllWhenReady();
        }
    }

    private void Attach(EditorViewModel? editor)
    {
        if (_editor != null)
        {
            _editor.SceneInvalidated -= InvalidateVisual;
            _editor.StructureChanged -= OnStructureChanged;
            _editor.ElementChanged -= OnElementChanged;
            _editor.FrameRequested -= Frame;
            _editor.CenterRequested -= CenterOn;
            _editor.PropertyChanged -= OnEditorPropertyChanged;
            _editor.Context.Textures.TextureLoaded -= TextureLoadedHandler;
            _editor.MoveVisualizer.Changed -= OnMoveVisualizerChanged;
            _editor.MoveVisualizer.FollowRequested -= OnMoveVisualizerFollow;
            EditorPreferences.Changed -= OnPreferencesChanged;
        }
        _editor = editor;
        _renderer = null;
        _framedOnce = false;
        if (editor != null)
        {
            _renderer = new SceneRenderer(editor);
            _renderer.RedrawRequested += () => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
            editor.SceneInvalidated += InvalidateVisual;
            editor.StructureChanged += OnStructureChanged;
            editor.ElementChanged += OnElementChanged;
            editor.FrameRequested += Frame;
            editor.CenterRequested += CenterOn;
            editor.PropertyChanged += OnEditorPropertyChanged;
            editor.Context.Textures.TextureLoaded += TextureLoadedHandler;
            editor.MoveVisualizer.Changed += OnMoveVisualizerChanged;
            editor.MoveVisualizer.FollowRequested += OnMoveVisualizerFollow;
            EditorPreferences.Changed += OnPreferencesChanged;
            _shownDocument = editor.Document;
            FrameAllWhenReady();
        }
        InvalidateVisual();
    }

    private void OnPreferencesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private sealed record ViewState(Point Center, double Zoom);

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SceneDocument, ViewState> _viewStates = new();
    private SceneDocument? _shownDocument;

    /// <summary>Looks at a level point without changing the zoom.</summary>
    public void CenterOn(Point level)
    {
        _center = level;
        _framedOnce = true;
        UpdateEditorCenter();
        InvalidateVisual();
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.PlacementType) or nameof(EditorViewModel.ActiveTool))
        {
            UpdateCursor(_lastPointer);
            InvalidateVisual();
        }
        else if (e.PropertyName == nameof(EditorViewModel.Document))
        {
            // Each document keeps the view it was left with (Object tab → Scene tab returns exactly there,
            // without passing through the level's camera or the origin first).
            if (_shownDocument != null && _framedOnce)
                _viewStates.AddOrUpdate(_shownDocument, new ViewState(_center, _zoom));
            _shownDocument = _editor?.Document;
            if (_shownDocument != null && _viewStates.TryGetValue(_shownDocument, out var state))
            {
                StopFrameAnimation();
                _center = state.Center;
                _zoom = state.Zoom;
                _framedOnce = true;
                UpdateEditorCenter();
                InvalidateVisual();
                return;
            }
            _framedOnce = false;
            FrameAllWhenReady();
        }
    }

    private void OnStructureChanged()
    {
        _renderer?.InvalidateStructure();
        InvalidateVisual();
    }

    private static readonly HashSet<string> StructuralProperties = new(StringComparer.Ordinal)
    {
        nameof(SceneElement.Layer), nameof(SceneElement.Order), nameof(ImageElement.Depth), nameof(SceneElement.Name),
        nameof(ItemElement.ItemType), nameof(ObjectElement.Reference),
    };

    private void OnElementChanged(SceneElement element, string? property)
    {
        if (_renderer == null)
            return;
        if (property == null || StructuralProperties.Contains(property))
            _renderer.InvalidateStructure();
        else
            _renderer.InvalidateElement(element);
        InvalidateVisual();
    }

    // ================= CAMERA ================= //

    private void UpdateEditorCenter()
    {
        if (_editor != null)
            _editor.SceneViewCenter = _center;
    }

    private void FrameAllWhenReady()
    {
        if (_editor == null || Bounds.Width <= 0 || _editor.IsLoading)
        {
            if (_editor != null && _editor.IsLoading)
            {
                // Retry once the level is loaded.
                void Handler(object? s, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == nameof(EditorViewModel.IsLoading) && !_editor!.IsLoading)
                    {
                        _editor.PropertyChanged -= Handler;
                        Dispatcher.UIThread.Post(FrameAllWhenReady, DispatcherPriority.Background);
                    }
                }
                _editor.PropertyChanged += Handler;
            }
            return;
        }
        _framedOnce = true;
        FrameStart();
    }

    /// <summary>Opens on the level's camera when it has one (like the game), otherwise on all Default-layer content.</summary>
    private void FrameStart()
    {
        if (_editor == null)
            return;
        var camera = _editor.Document.AllElements().OfType<CameraElement>()
            .OrderBy(c => c.Selection == ModeSelection.HunterMode ? 1 : 0).FirstOrDefault();
        if (camera != null)
        {
            _center = camera.WorldPosition;
            _zoom = Math.Clamp(Math.Min(Bounds.Width / (CameraElement.ViewSize.Width * 1.25), Bounds.Height / (CameraElement.ViewSize.Height * 1.25)), 0.02, 8);
        }
        else
        {
            var roots = _editor.Document.Roots.Where(r => Layers.GetFactor(r.LayerEditable ? r.Layer : Layers.Default) == 1).ToList();
            if (roots.Count > 0)
                FrameRect(roots.Select(SceneRenderer.WorldBounds).Aggregate((a, b) => a.Union(b)));
            else
            {
                _center = new Point(0, 0);
                _zoom = 0.5;
            }
        }
        UpdateEditorCenter();
        InvalidateVisual();
    }

    private void FrameRect(Rect rect)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        _center = rect.Center;
        double w = Math.Max(rect.Width, 50), h = Math.Max(rect.Height, 50);
        _zoom = Math.Clamp(Math.Min(Bounds.Width / (w * 1.2), Bounds.Height / (h * 1.2)), 0.01, 8);
        UpdateEditorCenter();
        InvalidateVisual();
    }

    /// <summary>Centers the view on an element (accounting for its layer's parallax).</summary>
    public void Frame(SceneElement element)
    {
        if (_renderer == null || Bounds.Width <= 0)
            return;
        var group = _renderer.GroupFor(element);
        var bounds = element is CameraElement camera
            ? new Rect(camera.WorldPosition.X - CameraElement.ViewSize.Width / 2, camera.WorldPosition.Y - CameraElement.ViewSize.Height / 2, CameraElement.ViewSize.Width, CameraElement.ViewSize.Height)
            : SceneRenderer.WorldBounds(element);
        double w = Math.Max(bounds.Width * group.FrameScale, 50), h = Math.Max(bounds.Height * group.FrameScale, 50);
        double zoom = Math.Clamp(Math.Min(Bounds.Width / (w * 1.6), Bounds.Height / (h * 1.6)), 0.01, 8);
        // Display = level·fs + C(1 − f·fs). The element is centred when its display point equals C,
        // i.e. c·fs + C(1 − f·fs) = C  →  C = c / f.
        var c = bounds.Center;
        var center = group.Factor > 1e-9 ? new Point(c.X / group.Factor, c.Y / group.Factor) : c;
        AnimateTo(center, zoom);
    }

    // ================= SMOOTH FRAMING (Unity's Frame Selected) ================= //

    /// <summary>Framing glides to its target; off for tests that need the view to move at once.</summary>
    public static bool AnimateFraming { get; set; } = true;

    private const double FrameDuration = 0.35;
    private DispatcherTimer? _frameTimer;
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private Point _frameFromCenter, _frameToCenter;
    private double _frameFromZoom, _frameToZoom;

    private void AnimateTo(Point center, double zoom)
    {
        StopFrameAnimation();
        if (!AnimateFraming || !IsVisible)
        {
            _center = center;
            _zoom = zoom;
            UpdateEditorCenter();
            InvalidateVisual();
            return;
        }
        _frameFromCenter = _center;
        _frameFromZoom = _zoom;
        _frameToCenter = center;
        _frameToZoom = zoom;
        _frameClock.Restart();
        _frameTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 60), DispatcherPriority.Render, (_, _) => StepFrameAnimation());
        _frameTimer.Start();
    }

    private void StepFrameAnimation()
    {
        double t = Math.Clamp(_frameClock.Elapsed.TotalSeconds / FrameDuration, 0, 1);
        double e = 1 - Math.Pow(1 - t, 3); // ease out
        // Zoom moves in log space so it feels even whether zooming in or out.
        _zoom = Math.Exp(Math.Log(_frameFromZoom) + (Math.Log(_frameToZoom) - Math.Log(_frameFromZoom)) * e);
        _center = new Point(_frameFromCenter.X + (_frameToCenter.X - _frameFromCenter.X) * e,
                            _frameFromCenter.Y + (_frameToCenter.Y - _frameFromCenter.Y) * e);
        UpdateEditorCenter();
        InvalidateVisual();
        if (t >= 1)
            StopFrameAnimation();
    }

    /// <summary>Any navigation by the user takes over from a framing in progress.</summary>
    private void StopFrameAnimation() => _frameTimer?.Stop();

    private void ZoomAt(Point screen, double factor)
    {
        StopFrameAnimation();
        double newZoom = Math.Clamp(_zoom * factor, 0.005, 16);
        var before = ScreenToDisplay(screen);
        _zoom = newZoom;
        var after = ScreenToDisplay(screen);
        _center += before - after;
        UpdateEditorCenter();
        InvalidateVisual();
    }

    private Point ScreenToDisplay(Point screen) => new((screen.X - Bounds.Width / 2) / _zoom + _center.X, (screen.Y - Bounds.Height / 2) / _zoom + _center.Y);

    /// <summary>Default-layer (factor 1) level point under a screen point.</summary>
    public Point ScreenToLevel(Point screen) => ScreenToDisplay(screen);

    // ================= RENDER ================= //

    public override void Render(DrawingContext context)
    {
        // The hidden view (Scene while the Game tab shows, or the Game view) still gets render requests from
        // every scene change; drawing the whole level for nothing would double the cost of each edit.
        if (!IsEffectivelyVisible)
            return;
        MarkFrameRendered();
        var size = Bounds.Size;
        context.FillRectangle(Background, new Rect(size));
        if (_renderer == null || _editor == null)
            return;

        if (IsGameView)
        {
            using (context.PushClip(new Rect(size)))
                RenderGame(context, size);
            UpdateAnimationClock();
            return;
        }

        var viewport = Viewport;
        using (context.PushClip(new Rect(size)))
        {
            _renderer.Render(context, viewport, c => RenderMoveVisualizerModel(c, viewport));
            RenderMoveVisualizer(context, viewport);
            _renderer.RenderSelection(context, viewport, _editor.Selection);
            RenderToolOverlay(context, viewport);
        }

        UpdateAnimationClock();
    }

    private void UpdateAnimationClock()
    {
        if (_renderer != null && _renderer.HasVisibleAnimation != _animating)
        {
            _animating = _renderer.HasVisibleAnimation;
            if (_animating) AnimationClock.Tick += OnAnimationTick;
            else AnimationClock.Tick -= OnAnimationTick;
        }
    }

    private void OnAnimationTick() => InvalidateVisual();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_animating)
        {
            AnimationClock.Tick -= OnAnimationTick;
            _animating = false;
        }
    }

    private static readonly Pen PathPen = new(new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)), 1.5, new DashStyle(new double[] { 5, 3 }, 0));
    private static readonly Pen PathPenInactive = new(new SolidColorBrush(Color.FromArgb(0x90, 0x80, 0x80, 0x80)), 1, new DashStyle(new double[] { 5, 3 }, 0));
    private static readonly Pen OnionPen = new(new SolidColorBrush(Color.FromArgb(0xA0, 0x30, 0x80, 0xFF)), 1, new DashStyle(new double[] { 3, 3 }, 0));
    private static readonly Pen SupportPen = new(new SolidColorBrush(Color.FromRgb(0xE0, 0x40, 0xE0)), 1);

    /// <summary>Dynamic Editor overlays: motion paths, onion-skin outlines and the custom-ease support handle.</summary>
    private void RenderDynamics(DrawingContext context, SceneViewport viewport)
    {
        if (_editor == null || _renderer == null || !_editor.Dynamics.IsBound)
            return;
        var dyn = _editor.Dynamics;
        foreach (var track in dyn.Tracks)
        {
            var element = track.Element;
            var m = viewport.LevelToScreen(_renderer.GroupFor(element));
            var origin = element.ParentWorldPosition;
            var pivotOffset = PivotToCenter(element);

            var path = dyn.SamplePath(track);
            if (path.Count > 1)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(path[0].X + origin.X + pivotOffset.X, path[0].Y + origin.Y + pivotOffset.Y).Transform(m), false);
                    for (int i = 1; i < path.Count; i++)
                        g.LineTo(new Point(path[i].X + origin.X + pivotOffset.X, path[i].Y + origin.Y + pivotOffset.Y).Transform(m));
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, track.IsActive ? PathPen : PathPenInactive, geometry);
                foreach (var key in track.Keys)
                {
                    var p = new Point(key.X + origin.X + pivotOffset.X, key.Y + origin.Y + pivotOffset.Y).Transform(m);
                    context.DrawEllipse(Brushes.White, track.IsActive ? PathPen : PathPenInactive, p, 3, 3);
                }
            }

            if (dyn.Onion)
            {
                foreach (var key in dyn.OnionKeys(track))
                {
                    double w = element.HasSize ? key.Width : 60, h = element.HasSize ? key.Height : 60;
                    double x = element.HasSize ? key.X : key.X - w / 2, y = element.HasSize ? key.Y : key.Y - h / 2;
                    var a = new Point(x + origin.X, y + origin.Y).Transform(m);
                    var b = new Point(x + w + origin.X, y + h + origin.Y).Transform(m);
                    context.DrawRectangle(null, OnionPen, RectEx.Box(a, b));
                }
            }

            if (track.IsActive && DynamicSupportHandle(viewport, track) is { } handle)
            {
                context.DrawLine(SupportPen, handle.A, handle.Handle);
                context.DrawLine(SupportPen, handle.Handle, handle.B);
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(0xE0, 0x40, 0xE0)), new Rect(handle.Handle.X - 4, handle.Handle.Y - 4, 8, 8));
            }
        }
    }

    /// <summary>Paths are drawn through the element's center (its pivot is often a corner).</summary>
    private static Vector PivotToCenter(SceneElement element)
    {
        if (!element.HasSize)
            return default;
        var b = element.GetLocalBounds();
        return new Vector(b.Center.X - element.X, b.Center.Y - element.Y);
    }

    private (Point A, Point Handle, Point B, ViewModels.Editor.DynamicKey KeyA, ViewModels.Editor.DynamicKey KeyB)? DynamicSupportHandle(SceneViewport viewport, ViewModels.Editor.DynamicTrack track)
    {
        if (_editor == null || _renderer == null || !_editor.Dynamics.CustomEase)
            return null;
        if (_editor.Dynamics.CurrentSegment(track) is not { } seg)
            return null;
        var element = track.Element;
        var m = viewport.LevelToScreen(_renderer.GroupFor(element));
        var origin = element.ParentWorldPosition;
        var off = PivotToCenter(element);
        var (cx, cy) = _editor.Dynamics.ControlPoint(seg.A, seg.B);
        Point P(double x, double y) => new Point(x + origin.X + off.X, y + origin.Y + off.Y).Transform(m);
        return (P(seg.A.X, seg.A.Y), P(cx, cy), P(seg.B.X, seg.B.Y), seg.A, seg.B);
    }

    private void RenderToolOverlay(DrawingContext context, SceneViewport viewport)
    {
        RenderDynamics(context, viewport);

        if (_mode == InteractionMode.RubberBand)
        {
            var r = RectEx.Box(_pressScreen, _lastPointer);
            context.FillRectangle(RubberBandFill, r);
            context.DrawRectangle(null, RubberBandPen, r);
        }
        else if (_mode == InteractionMode.PlaceDrag)
        {
            context.DrawRectangle(null, PlacementPen, RectEx.Box(_pressScreen, _lastPointer));
        }

        RenderPlacementGhost(context, viewport);
        RenderSnap(context);
        RenderGizmos(context, viewport);

        if (_editor == null || _renderer == null || _editor.Selection.Count == 0)
            return;

        // Rect tool: thin bounds of the selection (no gizmo, as requested).
        if (_editor.ActiveTool == EditorTool.Rect && _mode is InteractionMode.None or InteractionMode.RectTransform)
        {
            var b = _renderer.ScreenBounds(viewport, _editor.GetTopmostSelection());
            if (b.Width > 0 || b.Height > 0)
                context.DrawRectangle(null, HandlePen, b);
        }

        // Base-height handle of a single selected trapezoid.
        if (TrapezoidHandle(viewport) is { } handle)
        {
            context.DrawRectangle(HandleFill, HandlePen, handle);
        }

        if (_mode is InteractionMode.Rotate or InteractionMode.ModalRotate or InteractionMode.Scale or InteractionMode.ModalScale)
        {
            context.DrawEllipse(null, HandlePen, _pivotScreen, 4, 4);
            context.DrawLine(PlacementPen, _pivotScreen, _lastPointer);
        }
    }

    // ================= DROP FROM THE PROJECT WINDOW ================= //

    public bool DragOver(object payload, Point position)
    {
        if (IsGameView)
            return false;
        bool ok = payload is PwItem { CanPlace: true };
        if (ok && _editor != null && payload is PwItem item)
        {
            UpdateGhost(position, _editor.ProjectWindow.ElementType, ProjectWindowViewModel.SourceFor(item), item);
            _ghostFromDrag = _ghost != null;
        }
        else
            ClearGhost();
        return ok;
    }

    public void DragLeave() => ClearGhost();

    public async void Drop(object payload, Point position, KeyModifiers modifiers)
    {
        ClearGhost();
        if (IsGameView || _editor == null || payload is not PwItem { CanPlace: true } item)
            return;
        _editor.ProjectWindow.Select(item);
        var type = _editor.ActiveElementType?.Type ?? ElementType.Object;
        var source = _editor.ProjectWindow.CurrentSource;
        var created = await _editor.CreateElementAsync(type, ScreenToLevel(position), null, source);
        if (created != null)
            _editor.Select(created);
        // Dropping places one copy: it does not keep placement armed.
        _editor.ExitPlacement();
        Focus();
    }
}
