using System;
using System.Collections.Generic;
using Avalonia.Controls;

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
