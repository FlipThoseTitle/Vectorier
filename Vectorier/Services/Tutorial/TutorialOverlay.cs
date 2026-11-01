using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Vectorier.Services.Tutorial;

/// <summary>
/// The tutorial's layer over a window: everything is dimmed except the highlighted places (the "holes"), and a prompt
/// card explains the step. Clicks on the dimmed part are swallowed; clicks in a hole reach the editor below when the
/// step lets the user act there. The card itself is always clickable (Next, Exit Tutorial).
/// </summary>
public sealed class TutorialOverlay : Canvas, ICustomHitTest
{
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
    private static readonly IBrush BusyDim = new SolidColorBrush(Color.FromArgb(0xC8, 0x30, 0x30, 0x30));
    private static readonly IPen Outline = new Pen(new SolidColorBrush(Color.FromRgb(0x29, 0x9B, 0xFF)), 2);
    private const double HolePadding = 4;

    private readonly Border _card;
    private readonly TextBlock _title;
    private readonly TextBlock _text;
    private readonly TextBlock _progress;
    private readonly Button _next;
    private readonly Button _exit;
    private readonly Border _busy;
    private readonly TextBlock _busyText;

    private readonly DimLayer _dim;
    private List<Rect> _holes = new();
    private bool _interactive;
    private bool _dimmed = true;
    private bool _busyMode;

    public TutorialOverlay()
    {
        Focusable = false;
        ClipToBounds = false;
        _dim = new DimLayer(this) { IsHitTestVisible = false };
        Children.Add(_dim);

        _title = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        _text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        _progress = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65 };
        _next = new Button { Classes = { "accent" }, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        _next.Click += (_, _) => NextClicked?.Invoke();
        _exit = new Button { Classes = { "tool" }, FontSize = 11, Padding = new Thickness(6, 2) };
        _exit.Click += (_, _) => ExitClicked?.Invoke();

        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(_exit, Dock.Left);
        DockPanel.SetDock(_next, Dock.Right);
        DockPanel.SetDock(_progress, Dock.Right);
        _progress.Margin = new Thickness(0, 0, 10, 0);
        buttons.Children.Add(_exit);
        buttons.Children.Add(_next);
        buttons.Children.Add(_progress);

        _card = new Border
        {
            Width = 380,
            Padding = new Thickness(16, 14, 16, 12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 6 28 0 #80000000"),
            Child = new StackPanel { Spacing = 6, Children = { _title, _text, buttons } },
        };
        _card.Bind(Border.BackgroundProperty, _card.GetResourceObservable("AppPanelBackground"));
        _card.Bind(Border.BorderBrushProperty, _card.GetResourceObservable("AppAccent"));
        _title.Bind(TextBlock.ForegroundProperty, _title.GetResourceObservable("AppAccent"));
        Children.Add(_card);

        _busyText = new TextBlock { Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 520 };
        _busy = new Border { Child = _busyText, IsVisible = false };
        Children.Add(_busy);
    }

    public event Action? NextClicked;
    public event Action? ExitClicked;

    /// <summary>Shows a step. <paramref name="holes"/> are in this overlay's coordinates.</summary>
    public void Show(string title, string text, string? nextText, string exitText, string progress, IReadOnlyList<Rect> holes, bool interactive, bool dimmed)
    {
        _busyMode = false;
        _busy.IsVisible = false;
        _card.IsVisible = true;
        _title.Text = title;
        _title.IsVisible = !string.IsNullOrEmpty(title);
        _text.Text = text;
        _next.Content = nextText;
        _next.IsVisible = nextText != null;
        _exit.Content = exitText;
        _progress.Text = progress;
        bool changed = interactive != _interactive || dimmed != _dimmed || !holes.SequenceEqual(_holes);
        _holes = holes.Select(h => h.Inflate(HolePadding)).ToList();
        _interactive = interactive;
        _dimmed = dimmed;
        PlaceCard();
        if (changed)
            _dim.InvalidateVisual();
    }

    /// <summary>The whole window greyed out with a message (e.g. while the game runs).</summary>
    public void ShowBusy(string message)
    {
        if (_busyMode && _busyText.Text == message)
            return;
        _busyMode = true;
        _card.IsVisible = false;
        _busy.IsVisible = true;
        _busyText.Text = message;
        _holes = new List<Rect>();
        InvalidateArrange();
        _dim.InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        _dim.Arrange(new Rect(finalSize));
        PlaceCard();
        // Centered with the size being arranged (Bounds is still the previous one here).
        if (_busy.IsVisible)
        {
            var d = _busy.DesiredSize;
            _busy.Arrange(new Rect(Math.Max(0, (finalSize.Width - d.Width) / 2), Math.Max(0, (finalSize.Height - d.Height) / 2), d.Width, d.Height));
        }
        return size;
    }

    /// <summary>Next to the highlighted places (right, below, left, above), inside the window; centered without any.</summary>
    private void PlaceCard()
    {
        if (!_card.IsVisible || Bounds.Width <= 0)
            return;
        _card.Measure(Size.Infinity);
        var c = _card.DesiredSize;
        var area = new Rect(Bounds.Size).Deflate(12);
        Point at;
        if (_holes.Count == 0)
        {
            at = new Point((Bounds.Width - c.Width) / 2, (Bounds.Height - c.Height) / 2);
        }
        else
        {
            var u = _holes.Aggregate((a, b) => a.Union(b));
            const double gap = 14;
            var candidates = new[]
            {
                new Point(u.Right + gap, u.Top),
                new Point(u.Left, u.Bottom + gap),
                new Point(u.Left - gap - c.Width, u.Top),
                new Point(u.Left, u.Top - gap - c.Height),
            };
            at = candidates.Select(p => Clamp(p, c, area)).FirstOrDefault(p => !_holes.Any(h => h.Intersects(new Rect(p, c))), new Point(-1, -1));
            if (at.X < 0)
            {
                // Everything is covered: the corner of the window farthest from the highlight.
                var corners = new[] { area.TopLeft, new Point(area.Right - c.Width, area.Top), new Point(area.Left, area.Bottom - c.Height), new Point(area.Right - c.Width, area.Bottom - c.Height) };
                at = corners.OrderByDescending(p => Distance(new Rect(p, c).Center, u.Center)).First();
            }
        }
        SetLeft(_card, Math.Round(at.X));
        SetTop(_card, Math.Round(at.Y));
    }

    private static Point Clamp(Point p, Size c, Rect area) =>
        new(Math.Clamp(p.X, area.Left, Math.Max(area.Left, area.Right - c.Width)), Math.Clamp(p.Y, area.Top, Math.Max(area.Top, area.Bottom - c.Height)));

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    internal void DrawDim(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (_busyMode)
        {
            context.FillRectangle(BusyDim, bounds);
            return;
        }
        if (_dimmed)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.SetFillRule(FillRule.EvenOdd);
                AddRect(g, bounds);
                foreach (var hole in _holes)
                    AddRect(g, hole);
            }
            context.DrawGeometry(Dim, null, geometry);
        }
        foreach (var hole in _holes)
            context.DrawRectangle(null, Outline, hole, 4, 4);
    }

    private static void AddRect(StreamGeometryContext g, Rect r)
    {
        g.BeginFigure(r.TopLeft, true);
        g.LineTo(r.TopRight);
        g.LineTo(r.BottomRight);
        g.LineTo(r.BottomLeft);
        g.EndFigure(true);
    }

    /// <summary>
    /// The card and the busy message always take the pointer; a hole lets it through to the editor when the step is
    /// interactive; the rest is blocked when dimmed (an undimmed step only shows the card).
    /// </summary>
    public bool HitTest(Point point)
    {
        if (_card.IsVisible && _card.Bounds.Contains(point))
            return true;
        if (_busyMode)
            return true;
        if (!_dimmed)
            return false;
        if (_interactive && _holes.Any(h => h.Contains(point)))
            return false;
        return true;
    }
}

/// <summary>Draws the overlay's dimming (a Panel cannot render itself).</summary>
internal sealed class DimLayer : Control
{
    private readonly TutorialOverlay _owner;
    public DimLayer(TutorialOverlay owner) => _owner = owner;
    public override void Render(DrawingContext context) => _owner.DrawDim(context);
}
