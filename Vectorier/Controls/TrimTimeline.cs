using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Vectorier.Controls;

/// <summary>
/// Animation timeline with a trimmed range: drag the start / end handles to trim, click or drag elsewhere to move
/// the playhead. Values are frames from 0 to <see cref="Maximum"/>.
/// </summary>
public sealed class TrimTimeline : Control
{
    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<TrimTimeline, int>(nameof(Maximum));

    public static readonly StyledProperty<int> StartProperty =
        AvaloniaProperty.Register<TrimTimeline, int>(nameof(Start), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> EndProperty =
        AvaloniaProperty.Register<TrimTimeline, int>(nameof(End), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> CurrentProperty =
        AvaloniaProperty.Register<TrimTimeline, double>(nameof(Current), defaultBindingMode: BindingMode.TwoWay);

    private static readonly IBrush Track = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
    private static readonly IBrush Range = new SolidColorBrush(Color.FromRgb(0x29, 0x79, 0xFF), 0.35);
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromRgb(0x29, 0x79, 0xFF));
    private static readonly IPen PlayheadPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), 2);
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

    private enum Drag { None, Start, End, Playhead }
    private Drag _drag;

    static TrimTimeline()
    {
        AffectsRender<TrimTimeline>(MaximumProperty, StartProperty, EndProperty, CurrentProperty);
        HeightProperty.OverrideDefaultValue<TrimTimeline>(44);
    }

    public TrimTimeline()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public int Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Start { get => GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public int End { get => GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public double Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }

    private const double Pad = 10;
    private double TrackWidth => Math.Max(1, Bounds.Width - Pad * 2);
    private double X(double frame) => Pad + (Maximum <= 0 ? 0 : frame / Maximum * TrackWidth);
    private int FrameAt(double x) => Maximum <= 0 ? 0 : (int)Math.Round(Math.Clamp((x - Pad) / TrackWidth, 0, 1) * Maximum);

    public override void Render(DrawingContext context)
    {
        double y = 16;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        context.FillRectangle(Track, new Rect(Pad, y - 3, TrackWidth, 6), 3);
        double xs = X(Start), xe = X(End);
        context.FillRectangle(Range, new Rect(xs, y - 7, Math.Max(2, xe - xs), 14), 2);

        // Ticks every 10 frames with labels every so often.
        int step = Maximum > 200 ? 50 : Maximum > 80 ? 20 : 10;
        for (int f = 0; f <= Maximum; f += step)
        {
            double x = X(f);
            context.FillRectangle(TickBrush, new Rect(x - 0.5, y + 8, 1, 4));
            var text = new FormattedText(f.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 9, TickBrush);
            context.DrawText(text, new Point(x - text.Width / 2, y + 13));
        }

        DrawHandle(context, xs, y, true);
        DrawHandle(context, xe, y, false);
        double xp = X(Current);
        context.DrawLine(PlayheadPen, new Point(xp, y - 11), new Point(xp, y + 11));
    }

    private static void DrawHandle(DrawingContext context, double x, double y, bool start)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double dir = start ? -1 : 1;
            c.BeginFigure(new Point(x, y - 10), true);
            c.LineTo(new Point(x + dir * 7, y - 10));
            c.LineTo(new Point(x + dir * 7, y + 10));
            c.LineTo(new Point(x, y + 10));
            c.EndFigure(true);
        }
        context.DrawGeometry(HandleBrush, null, g);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        double ds = Math.Abs(p.X - (X(Start) - 3.5)), de = Math.Abs(p.X - (X(End) + 3.5));
        if (ds <= 8 && ds <= de) _drag = Drag.Start;
        else if (de <= 8) _drag = Drag.End;
        else _drag = Drag.Playhead;
        Apply(p.X);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag != Drag.None)
            Apply(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }

    private void Apply(double x)
    {
        int f = FrameAt(x);
        switch (_drag)
        {
            case Drag.Start:
                Start = Math.Min(f, End);
                break;
            case Drag.End:
                End = Math.Max(f, Start);
                break;
            case Drag.Playhead:
                Current = Math.Clamp(f, Start, End);
                break;
        }
    }
}
