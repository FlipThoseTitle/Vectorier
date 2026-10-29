using System.Collections.Generic;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Views.Editor;

public partial class EditorView : UserControl
{
    private EditorViewModel? _vm;
    private readonly DispatcherTimer _infoTimer;

    public EditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        ElementBar.AddHandler(PointerPressedEvent, OnEyePressed, RoutingStrategies.Tunnel);
        ElementBar.AddHandler(PointerMovedEvent, OnEyeMoved, RoutingStrategies.Tunnel);
        ElementBar.AddHandler(PointerReleasedEvent, OnEyeReleased, RoutingStrategies.Tunnel);
        // Inputs outside the Scene (sliders, number drags, spinners, Hierarchy drags) are one undo step each:
        // the step is committed when the button is released.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnAnyPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Scene.ContextMenuRequested += (element, level) => HierarchyPanel.ShowContextMenu(element, Scene, level);
        _infoTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => UpdateInfo());
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as EditorViewModel;
        if (_vm != null)
            _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateToolButtons();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _infoTimer.Start();
        Dispatcher.UIThread.Post(() => Scene.Focus(), DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _infoTimer.Stop();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.ActiveTool))
            UpdateToolButtons();
    }

    private void UpdateToolButtons()
    {
        var tool = _vm?.ActiveTool ?? EditorTool.Move;
        MoveTool.IsChecked = tool == EditorTool.Move;
        RotateTool.IsChecked = tool == EditorTool.Rotate;
        ScaleTool.IsChecked = tool == EditorTool.Scale;
        RectTool.IsChecked = tool == EditorTool.Rect;
    }

    private bool _uiGesture;
    private bool _projectWindowActive;

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null)
            return;
        if (_uiGesture)
        {
            _uiGesture = false;
            _vm.EndGesture();
        }
        _projectWindowActive = (e.Source as Visual)?.FindAncestorOfType<ProjectWindowView>(true) != null;
        // The Scene runs its own gestures.
        if ((e.Source as Visual)?.FindAncestorOfType<Controls.Scene.SceneView>(true) != null)
            return;
        _uiGesture = true;
        _vm.BeginGesture();
    }

    private void OnAnyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Tunnel: the gesture ends before a button handles its click (Undo must not run inside a gesture).
        if (_vm == null || !_uiGesture)
            return;
        _uiGesture = false;
        _vm.EndGesture();
    }

    private void UpdateInfo()
    {
        if (Scene.Renderer is { } renderer)
        {
            ZoomText.Text = $"{Scene.Zoom * 100:0.#}%";
        }
        // Toggle buttons uncheck themselves on click; keep them in sync with the active tool.
        UpdateToolButtons();
    }

    // ================= ELEMENT BAR EYES (click, or drag across several like Blender / Unity) ================= //

    private bool? _eyePaint;

    private static ElementTypeInfo? EyeAt(Visual? visual)
    {
        while (visual != null)
        {
            if (visual is Border { Tag: ElementTypeInfo info } border && border.Classes.Contains("eye"))
                return info;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    private void OnEyePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ElementBar).Properties.IsLeftButtonPressed || EyeAt(e.Source as Visual) is not { } info)
            return;
        _eyePaint = !info.IsVisible;
        info.IsVisible = _eyePaint.Value;
        e.Pointer.Capture(ElementBar);
        e.Handled = true;
    }

    private void OnEyeMoved(object? sender, PointerEventArgs e)
    {
        if (_eyePaint is not { } value)
            return;
        // While captured, find the eye under the pointer by position.
        var hit = ElementBar.InputHitTest(e.GetPosition(ElementBar)) as Visual;
        if (EyeAt(hit) is { } info && info.IsVisible != value)
            info.IsVisible = value;
        e.Handled = true;
    }

    private void OnEyeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_eyePaint == null)
            return;
        _eyePaint = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnElementTypeClick(object? sender, RoutedEventArgs e)
    {
        if (_vm != null && sender is Control { Tag: ElementTypeInfo info })
            _vm.ChooseElementType(info);
    }

    /// <summary>Move Visualizer: releasing C stops sticking the placement preview to platforms.</summary>
    private readonly HashSet<Key> _heldKeys = new();

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);
        if (e.Key == Key.A)
            Scene.SetSnapScaleModifier(false);
        if (e.Key is Key.V or Key.C && Scene.IsSnapping)
        {
            Scene.EndSnapKey(e.Key == Key.C);
            e.Handled = true;
            return;
        }
        if (_vm != null && e.Key == Key.C && _vm.MoveVisualizer.PlatformSnapHeld)
        {
            _vm.MoveVisualizer.PlatformSnapHeld = false;
            e.Handled = true;
        }
    }

    /// <summary>Editor shortcuts. Text fields keep their own keys (typing, Ctrl+C...).</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null)
            return;

        var mods = e.KeyModifiers;
        bool ctrl = (mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool shift = (mods & KeyModifiers.Shift) != 0;
        bool inText = e.Source is TextBox || (e.Source as Visual)?.FindAncestorOfType<TextBox>() != null
                      || e.Source is NumericUpDown || (e.Source as Visual)?.FindAncestorOfType<NumericUpDown>() != null;

        // Shortcuts that work everywhere.
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.S:
                    if (shift) _vm.SaveAsCommand.Execute(null);
                    else _vm.SaveCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.N when !inText:
                    _vm.NewLevelCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.O when !inText:
                    _vm.OpenCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }
        if (e.Key == Key.F5 && _vm.CanPlay)
        {
            if (shift) _vm.PlayLevelHunterCommand.Execute(null);
            else _vm.PlayLevelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (inText)
            return;

        // Modal Scene transforms take keys first (Esc, Enter, X, Y).
        if (Scene.IsModal && Scene.HandleKey(e.Key, mods))
        {
            e.Handled = true;
            return;
        }

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z when shift: _vm.Redo(); break;
                case Key.Z: _vm.Undo(); break;
                case Key.Y: _vm.Redo(); break;
                case Key.X: _vm.CutCommand.Execute(null); break;
                case Key.C: _vm.CopyCommand.Execute(null); break;
                case Key.V when shift: _vm.PasteIntoCommand.Execute(null); break;
                case Key.V: _vm.PasteCommand.Execute(null); break;
                case Key.D when shift: _vm.ClearSelection(); break;
                case Key.D: _vm.Duplicate(); break;
                case Key.A: _vm.SelectAllCommand.Execute(null); break;
                case Key.G when shift: _vm.Ungroup(); break;
                case Key.G: _vm.GroupCommand.Execute(null); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        // Move Visualizer placement: hold C to stick to platforms, Esc stops placing.
        if (_vm.MoveVisualizer.IsPlacementActive)
        {
            if (e.Key == Key.C && mods == KeyModifiers.None)
            {
                _vm.MoveVisualizer.PlatformSnapHeld = true;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                _vm.MoveVisualizer.StopPlacement();
                e.Handled = true;
                return;
            }
        }

        // Transform snapping over the Scene: hold V (everything) or C (Platforms), A to scale.
        if (e.Key == Key.A && mods == KeyModifiers.None)
        {
            Scene.SetSnapScaleModifier(true);
            if (Scene.IsSnapping)
            {
                e.Handled = true;
                return;
            }
        }
        if (e.Key is Key.V or Key.C && mods == KeyModifiers.None)
        {
            if (Scene.IsSnapping || Scene.BeginSnapKey(e.Key == Key.C))
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key is Key.Delete or Key.Back && mods == KeyModifiers.None)
        {
            // Last clicked in the Project Window: Delete removes its selected files / folders or assets.
            if (_projectWindowActive && (_vm.ProjectWindow.LastClickInFolders ? _vm.ProjectWindow.SelectedFolders.Count > 0 : _vm.ProjectWindow.SelectedItems.Count > 0))
            {
                _ = _vm.ProjectWindow.DeleteSelectionAsync();
                e.Handled = true;
                return;
            }
            _vm.DeleteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (!Scene.CancelCurrent())
            {
                if (_vm.IsPlacing) _vm.ExitPlacement();
                else _vm.ClearSelection();
            }
            e.Handled = true;
            return;
        }

        // [ and ] change the Order in Layer of the selection by one per tap (holding the key does not repeat).
        int orderDelta = !ctrl && (mods & KeyModifiers.Alt) == 0 ? e.KeySymbol switch
        {
            "[" => -1,
            "]" => 1,
            _ => e.Key == Key.OemOpenBrackets ? -1 : e.Key == Key.OemCloseBrackets ? 1 : 0,
        } : 0;
        if (orderDelta != 0)
        {
            if (_heldKeys.Add(e.Key))
                _vm.ChangeOrder(orderDelta);
            e.Handled = true;
            return;
        }

        // Z toggles Center / Pivot, X toggles Global / Local (like Unity).
        if (mods == KeyModifiers.None && e.Key is Key.Z or Key.X)
        {
            if (_heldKeys.Add(e.Key))
            {
                if (e.Key == Key.Z) _vm.UsePivot = !_vm.UsePivot;
                else _vm.IsLocalMode = !_vm.IsLocalMode;
                _vm.StatusText = e.Key == Key.Z ? (_vm.UsePivot ? "Pivot" : "Center") : (_vm.IsLocalMode ? "Local" : "Global");
                Scene.InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        // Blender-style G / R / S over the Scene.
        if (Scene.IsPointerInside && mods == KeyModifiers.None && e.Key is Key.G or Key.R or Key.S)
        {
            if (Scene.HandleKey(e.Key, mods))
            {
                e.Handled = true;
                return;
            }
        }

        if (mods == KeyModifiers.None)
        {
            // Q / W / E / T (R is left to Blender's rotate; panning is the right mouse button).
            EditorTool? tool = e.Key switch
            {
                Key.Q => EditorTool.Move,
                Key.W => EditorTool.Rotate,
                Key.E => EditorTool.Scale,
                Key.T => EditorTool.Rect,
                _ => null,
            };
            if (tool != null)
            {
                _vm.SelectTool(tool.Value);
                e.Handled = true;
                return;
            }

            // Number keys pick the Element Bar entries (1 = Object ... 0 = the tenth).
            int index = e.Key switch
            {
                >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
                Key.D0 => 9,
                >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
                Key.NumPad0 => 9,
                _ => -1,
            };
            if (index >= 0 && index < _vm.ElementTypes.Count)
            {
                _vm.ChooseElementType(_vm.ElementTypes[index]);
                e.Handled = true;
                return;
            }
        }

        // Shift + 1..3 pick the 11th to 13th entries (Animation, Particle, Comment).
        if (mods == KeyModifiers.Shift && e.Key is >= Key.D1 and <= Key.D3)
        {
            int index = 10 + (e.Key - Key.D1);
            if (index < _vm.ElementTypes.Count)
            {
                _vm.ChooseElementType(_vm.ElementTypes[index]);
                e.Handled = true;
                return;
            }
        }

        if (Scene.IsPointerInside || Scene.IsFocused)
        {
            if (Scene.HandleKey(e.Key, mods))
                e.Handled = true;
        }
    }
}
