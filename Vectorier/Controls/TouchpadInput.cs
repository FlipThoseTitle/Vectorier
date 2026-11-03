using System;
using Avalonia.Input;
using Vectorier.Services;

namespace Vectorier.Controls;

/// <summary>
/// Tells touchpad gestures apart from a mouse wheel. Avalonia delivers a two-finger scroll as wheel events and, on
/// Windows and in browsers, a pinch as Ctrl + wheel; macOS sends a pinch as a separate magnify gesture.
/// </summary>
public static class TouchpadInput
{
    // A touchpad stream stays a touchpad stream for this long, so the odd whole-step event inside it does not zoom.
    private const long StickyMs = 400;
    private static long _touchpadUntil;

    /// <summary>The wheel event is a two-finger scroll (pan) rather than a mouse wheel notch (zoom).</summary>
    public static bool IsTouchpadScroll(PointerWheelEventArgs e)
    {
        switch (EditorPreferences.ScrollNavigation)
        {
            case ScrollNavigation.Mouse: return false;
            case ScrollNavigation.Touchpad: return true;
        }
        // Wheel notches are whole steps on one axis; a touchpad sends fractions, often on both axes.
        var d = e.Delta;
        long now = Environment.TickCount64;
        if (d.X != 0 || Math.Abs(d.Y - Math.Round(d.Y)) > 1e-3)
            _touchpadUntil = now + StickyMs;
        return now < _touchpadUntil;
    }

    /// <summary>Ctrl + wheel: a pinch on a Windows or browser touchpad (or Ctrl + mouse wheel).</summary>
    public static bool IsPinch(PointerWheelEventArgs e) => (e.KeyModifiers & KeyModifiers.Control) != 0;

    /// <summary>Zoom factor of a wheel event: a whole notch zooms by <paramref name="notch"/>, a pinch continuously.</summary>
    public static double WheelZoomFactor(PointerWheelEventArgs e, double notch = 1.15)
    {
        double dy = e.Delta.Y;
        // A pinch arrives as many small fractional steps; give them a little more weight so a pinch
        // covers about the same range as on the touchpad itself.
        bool fractional = Math.Abs(dy - Math.Round(dy)) > 1e-3;
        return Math.Pow(fractional ? notch * notch : notch, dy);
    }

    /// <summary>Zoom factor of a macOS magnify gesture (its delta is the change in magnification).</summary>
    public static double MagnifyFactor(PointerDeltaEventArgs e) => Math.Exp(e.Delta.X);

    /// <summary>Screen pixels a two-finger scroll moves per wheel unit (Avalonia's own scroll step).</summary>
    public const double PixelsPerUnit = 50;
}
