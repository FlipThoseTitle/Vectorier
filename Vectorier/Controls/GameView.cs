using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vectorier.Gameplay.Rl;
using Vectorier.Services.Game;

namespace Vectorier.Controls;

/// <summary>
/// The game's window inside the editor: while a level is played (<see cref="GameHost"/>), every display frame runs
/// the game's frame and draws it here; the keyboard and the mouse (its debug buttons) go to the game.
/// </summary>
public sealed class GameView : Control
{
    private bool _frameRequested;

    public GameView()
    {
        Focusable = true;
        ClipToBounds = true;
        GameHost.Current.RunningChanged += OnRunningChanged;
        // Keys held when the view loses the keyboard are let go (they would stay "down" for the game).
        LostFocus += (_, _) => GameHost.Current.ReleaseKeys();
    }

    private void OnRunningChanged()
    {
        InvalidateVisual();
        RequestFrame();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestFrame();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            RequestFrame();
    }

    /// <summary>Asks for the next display frame while the game runs and this view is shown.</summary>
    private void RequestFrame()
    {
        if (_frameRequested || !GameHost.Current.IsRunning || !IsEffectivelyVisible)
            return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null)
            return;
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            InvalidateVisual();
        });
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(Brushes.Gray, new Rect(size));
        if (!GameHost.Current.IsRunning || size.Width < 2 || size.Height < 2)
            return;
        GameHost.Current.Frame(context, (int)size.Width, (int)size.Height);
        RequestFrame();
    }

    // ================= INPUT ================= //

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!GameHost.Current.IsRunning)
            return;
        var key = Map(e.Key);
        if (key != KeyboardKey.Null)
        {
            GameHost.Current.KeyDown(key);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        var key = Map(e.Key);
        if (key != KeyboardKey.Null)
        {
            GameHost.Current.KeyUp(key);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Move(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        Move(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Raylib.MouseButtonChanged(Vectorier.Gameplay.Rl.MouseButton.Left, true);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Move(e);
        if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left)
            Raylib.MouseButtonChanged(Vectorier.Gameplay.Rl.MouseButton.Left, false);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Raylib.MouseMoved(new Vector2(-1, -1));
    }

    private void Move(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        Raylib.MouseMoved(new Vector2((float)p.X, (float)p.Y));
    }

    /// <summary>The editor's key → Raylib's (the game reads Raylib's codes).</summary>
    public static KeyboardKey Map(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (KeyboardKey)((int)KeyboardKey.A + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (KeyboardKey)((int)KeyboardKey.Zero + (key - Key.D0)),
        >= Key.F1 and <= Key.F12 => (KeyboardKey)((int)KeyboardKey.F1 + (key - Key.F1)),
        Key.Up => KeyboardKey.Up,
        Key.Down => KeyboardKey.Down,
        Key.Left => KeyboardKey.Left,
        Key.Right => KeyboardKey.Right,
        Key.Escape => KeyboardKey.Escape,
        Key.Space => KeyboardKey.Space,
        Key.Enter => KeyboardKey.Enter,
        Key.Tab => KeyboardKey.Tab,
        Key.Back => KeyboardKey.Backspace,
        Key.LeftShift => KeyboardKey.LeftShift,
        Key.RightShift => KeyboardKey.RightShift,
        Key.LeftCtrl => KeyboardKey.LeftControl,
        Key.RightCtrl => KeyboardKey.RightControl,
        Key.LeftAlt => KeyboardKey.LeftAlt,
        Key.RightAlt => KeyboardKey.RightAlt,
        _ => KeyboardKey.Null,
    };
}
