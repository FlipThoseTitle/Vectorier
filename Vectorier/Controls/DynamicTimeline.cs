using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls;

/// <summary>
/// Keyframe strip of one Dynamic Editor track: ruler, keys, playhead and the dead space after the track's end.
/// Click to scrub, click keys to select (Ctrl toggles, Shift adds), drag keys to move them, wheel to scroll
/// (Ctrl + wheel zooms).
/// </summary>
public sealed class DynamicTimeline : Control
{
    public static readonly StyledProperty<DynamicTrack?> TrackProperty =
        AvaloniaProperty.Register<DynamicTimeline, DynamicTrack?>(nameof(Track));

    public static readonly StyledProperty<DynamicEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<DynamicTimeline, DynamicEditorViewModel?>(nameof(Editor));

    private const double Inset = 6;
    private const double RulerHeight = 16;
    private const double HandleHeight = 14;

    private static readonly Color PlayheadColor = Color.FromRgb(0xE8, 0x3B, 0x3B);
    private static readonly Color PlayheadParkedColor = Color.FromArgb(0x90, 0xE8, 0x3B, 0x3B);

    private static readonly Typeface Font = new(FontFamily.Default);

    private bool _scrubbing;
    private bool _movingKeys;
    private int _dragStartFrame;
    private List<(DynamicKey Key, int OriginalFrame)> _moving = new();

    static DynamicTimeline()
    {
        ClipToBoundsProperty.OverrideDefaultValue<DynamicTimeline>(true);
        FocusableProperty.OverrideDefaultValue<DynamicTimeline>(true);
        // macOS touchpad pinch zooms like Ctrl + wheel (Windows and browsers already send a pinch as Ctrl + wheel).
        PointerTouchPadGestureMagnifyEvent.AddClassHandler<DynamicTimeline>((t, e) =>
        {
            if (t.Editor is { } editor)
            {
                editor.Zoom = Math.Clamp(editor.Zoom * TouchpadInput.MagnifyFactor(e), 0.2, 8);
                e.Handled = true;
            }
        });
    }

    public DynamicTrack? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public DynamicEditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TrackProperty)
        {
            if (change.OldValue is DynamicTrack old) old.KeysChanged -= InvalidateVisual;
            if (change.NewValue is DynamicTrack t) t.KeysChanged += InvalidateVisual;
            InvalidateVisual();
        }
        else if (change.Property == EditorProperty)
        {
            if (change.OldValue is DynamicEditorViewModel old)
            {
                old.TimelineChanged -= InvalidateVisual;
                old.PropertyChanged -= OnEditorChanged;
            }
            if (change.NewValue is DynamicEditorViewModel e)
            {
                e.TimelineChanged += InvalidateVisual;
                e.PropertyChanged += OnEditorChanged;
            }
            InvalidateVisual();
        }
    }

    private void OnEditorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DynamicEditorViewModel.Frame) or nameof(DynamicEditorViewModel.Zoom) or nameof(DynamicEditorViewModel.ScrollFrame))
            InvalidateVisual();
    }

    private double Ppf => Math.Max(2, 10 * (Editor?.Zoom ?? 1));

    private Rect Inner => new(Inset, RulerHeight, Math.Max(1, Bounds.Width - Inset * 2), Math.Max(1, Bounds.Height - RulerHeight - 4));

    private double VisibleFrames => Math.Max(1, Inner.Width / Ppf);

    private double Scroll
    {
        get
        {
            var e = Editor;
            if (e == null) return 0;
            return Math.Clamp(e.ScrollFrame, 0, Math.Max(0, e.MaxEnd - VisibleFrames));
        }
    }

    private int FrameAt(double x) => Math.Clamp((int)Math.Round(Scroll + (x - Inner.X) / Ppf), 0, Editor?.MaxEnd ?? 0);

    private double XOf(double frame) => Inner.X + (frame - Scroll) * Ppf;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        bool dark = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var bg = new SolidColorBrush(dark ? Color.FromRgb(0x25, 0x25, 0x25) : Color.FromRgb(0xF7, 0xF7, 0xF7));
        var fg = new SolidColorBrush(dark ? Color.FromRgb(0xB0, 0xB0, 0xB0) : Color.FromRgb(0x60, 0x60, 0x60));
        var grid = new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0, 0, 0)), 1);
        context.FillRectangle(bg, bounds);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(dark ? Color.FromRgb(0x40, 0x40, 0x40) : Color.FromRgb(0xC8, 0xC8, 0xC8)), 1), bounds.Deflate(0.5));

        var editor = Editor;
        var track = Track;
        if (editor == null || track == null)
            return;

        var inner = Inner;
        double scroll = Scroll;
        double visible = VisibleFrames;

        // Dead space after the track's own end.
        double endX = XOf(track.TotalFrames);
        if (endX < inner.Right)
        {
            double x0 = Math.Max(endX, inner.X);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), new Rect(x0, inner.Y, inner.Right - x0, inner.Height));
        }

        // Ruler: ten marks across the visible range (numbers under the playhead's tab are left out).
        int trackFrame = editor.TrackFrame(track);
        var tabRect = PlayheadTab(XOf(trackFrame), trackFrame, bounds).Inflate(new Thickness(2, 0));
        const int marks = 10;
        for (int i = 0; i <= marks; i++)
        {
            double t = i / (double)marks;
            double x = Math.Round(inner.X + t * inner.Width) + 0.5;
            context.DrawLine(grid, new Point(x, inner.Y), new Point(x, inner.Bottom));
            int frame = (int)Math.Round(scroll + visible * t);
            var text = new FormattedText(frame.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 10, fg);
            var at = new Point(Math.Min(x + 2, bounds.Width - text.Width - 2), 2);
            if (!new Rect(at, new Size(text.Width, text.Height)).Intersects(tabRect))
                context.DrawText(text, at);
        }

        // Keys.
        double rowY = inner.Y + inner.Height / 2;
        foreach (var key in track.Keys)
        {
            if (key.Frame < scroll - 1 || key.Frame > scroll + visible + 1)
                continue;
            double x = XOf(key.Frame);
            var r = new Rect(x - 5, rowY - 8, 10, 16);
            bool selected = track.SelectedFrames.Contains(key.Frame);
            var color = selected ? Color.FromRgb(0xFF, 0xD9, 0x40) : key.Frame == trackFrame ? Color.FromRgb(0xFF, 0x66, 0x66) : (dark ? Color.FromRgb(0xC8, 0xC8, 0xC8) : Color.FromRgb(0x8A, 0x8A, 0x8A));
            context.FillRectangle(new SolidColorBrush(color), r);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), 1), r);
        }

        // Playhead (parked on the track's last frame when the master frame is beyond it).
        DrawPlayhead(context, XOf(trackFrame), trackFrame, editor.Frame > track.TotalFrames, inner, bounds);
    }

    /// <summary>
    /// Scrub handle: a rounded tab in the ruler showing the frame, with a pointer and a line down through the keys,
    /// so it reads as something to grab and drag (the whole strip scrubs too).
    /// </summary>
    private void DrawPlayhead(DrawingContext context, double x, int frame, bool parked, Rect inner, Rect bounds)
    {
        if (x < inner.X - 1 || x > inner.Right + 1)
            return;
        var color = parked ? PlayheadParkedColor : PlayheadColor;
        var brush = new SolidColorBrush(color);
        double px = Math.Round(x) + 0.5;

        // Soft shadow, then the line.
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), 3), new Point(px + 1, inner.Y), new Point(px + 1, inner.Bottom));
        context.DrawLine(new Pen(brush, _hoverPlayhead || _scrubbing ? 2 : 1.5), new Point(px, inner.Y), new Point(px, inner.Bottom));

        var label = new FormattedText(frame.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 10, Brushes.White);
        var tab = PlayheadTab(x, frame, bounds);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            // Rounded tab with a small pointer under it, aimed at the frame.
            double r = 3, bottom = tab.Bottom;
            double tip = Math.Clamp(px, tab.X + r + 3, tab.Right - r - 3);
            g.BeginFigure(new Point(tab.X + r, tab.Y), true);
            g.LineTo(new Point(tab.Right - r, tab.Y));
            g.ArcTo(new Point(tab.Right, tab.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(tab.Right, bottom - r));
            g.ArcTo(new Point(tab.Right - r, bottom), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(tip + 3, bottom));
            g.LineTo(new Point(px, bottom + 3));
            g.LineTo(new Point(tip - 3, bottom));
            g.LineTo(new Point(tab.X + r, bottom));
            g.ArcTo(new Point(tab.X, bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(tab.X, tab.Y + r));
            g.ArcTo(new Point(tab.X + r, tab.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
        context.DrawText(label, new Point(tab.X + (tab.Width - label.Width) / 2, tab.Y + (tab.Height - label.Height) / 2));
    }

    /// <summary>The rounded tab of the playhead in the ruler (sized to its frame number).</summary>
    private static Rect PlayheadTab(double x, int frame, Rect bounds)
    {
        var label = new FormattedText(frame.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 10, Brushes.White);
        double w = Math.Max(18, label.Width + 8);
        double px = Math.Round(x) + 0.5;
        double left = Math.Clamp(px - w / 2, 1, Math.Max(1, bounds.Width - w - 1));
        return new Rect(left, 1, w, HandleHeight);
    }

    private bool _hoverPlayhead;

    private bool OverPlayhead(Point pos)
    {
        var editor = Editor;
        var track = Track;
        if (editor == null || track == null)
            return false;
        double px = XOf(editor.TrackFrame(track));
        return Math.Abs(pos.X - px) <= 8 && pos.Y <= RulerHeight + 2 || Math.Abs(pos.X - px) <= 3;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var editor = Editor;
        var track = Track;
        if (editor == null || track == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        editor.ActiveTrack = track;
        editor.StopPreview();
        var pos = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;

        bool ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        // Key under the pointer? (The playhead's tab in the ruler always scrubs.)
        double rowY = Inner.Y + Inner.Height / 2;
        var hit = pos.Y <= RulerHeight ? null : track.Keys.LastOrDefault(k => Math.Abs(XOf(k.Frame) - pos.X) <= 6 && Math.Abs(pos.Y - rowY) <= 10);
        if (hit != null)
        {
            if (ctrl)
            {
                if (!track.SelectedFrames.Remove(hit.Frame)) track.SelectedFrames.Add(hit.Frame);
            }
            else if (shift)
            {
                track.SelectedFrames.Add(hit.Frame);
            }
            else if (!track.SelectedFrames.Contains(hit.Frame))
            {
                track.SelectedFrames.Clear();
                track.SelectedFrames.Add(hit.Frame);
            }
            _movingKeys = true;
            editor.BeginEdit(); // the drag is one undo step
            _dragStartFrame = FrameAt(pos.X);
            _moving = track.Keys.Where(k => track.SelectedFrames.Contains(k.Frame)).Select(k => (k, k.Frame)).ToList();
            editor.SetFrame(hit.Frame);
            InvalidateVisual();
            return;
        }

        _scrubbing = true;
        if (!ctrl && !shift)
            track.SelectedFrames.Clear();
        editor.SetFrame(FrameAt(pos.X));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var editor = Editor;
        var track = Track;
        if (editor == null || track == null)
            return;
        var pos = e.GetPosition(this);
        bool hover = OverPlayhead(pos);
        if (hover != _hoverPlayhead)
        {
            _hoverPlayhead = hover;
            Cursor = hover ? new Cursor(StandardCursorType.SizeWestEast) : null;
            InvalidateVisual();
        }
        if (_scrubbing)
        {
            editor.SetFrame(FrameAt(pos.X));
        }
        else if (_movingKeys && _moving.Count > 0)
        {
            int delta = FrameAt(pos.X) - _dragStartFrame;
            editor.MoveKeys(track, _moving, delta, merge: false);
            editor.SetFrame(Math.Clamp(FrameAt(pos.X), 0, editor.MaxEnd));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var editor = Editor;
        var track = Track;
        if (_movingKeys && editor != null && track != null && _moving.Count > 0)
        {
            int delta = FrameAt(e.GetPosition(this).X) - _dragStartFrame;
            if (delta != 0)
                editor.MoveKeys(track, _moving, delta, merge: true);
        }
        if (_movingKeys)
            editor?.EndEdit();
        _scrubbing = false;
        _movingKeys = false;
        _moving.Clear();
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverPlayhead)
        {
            _hoverPlayhead = false;
            Cursor = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_movingKeys)
            Editor?.EndEdit();
        _scrubbing = false;
        _movingKeys = false;
        _moving.Clear();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var editor = Editor;
        if (editor == null)
            return;
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            editor.Zoom = Math.Clamp(editor.Zoom * Math.Pow(1.2, e.Delta.Y), 0.2, 8);
        else if ((e.KeyModifiers & KeyModifiers.Shift) != 0 || e.Delta.X != 0)
            // Only Shift + wheel (or a horizontal wheel / trackpad swipe) scrolls the timeline; a plain wheel is left
            // to the list so many animated elements can be scrolled through.
            editor.ScrollFrame = Math.Clamp(Scroll - (e.Delta.Y + e.Delta.X) * VisibleFrames * 0.1, 0, Math.Max(0, editor.MaxEnd - VisibleFrames));
        else
            return;
        e.Handled = true;
    }
}
