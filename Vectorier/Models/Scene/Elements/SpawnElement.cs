using System;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

/// <summary>&lt;Spawn&gt;: a named spawn point with a start animation ("Move|Frame").</summary>
public sealed partial class SpawnElement : PointElement
{
    public const string DefaultName = "DefaultSpawn";
    public const string DefaultAnimation = "JumpOff|18";

    public override ElementType Type => ElementType.Spawn;
    public override Size HandleSize => new(100, 100);

    [ObservableProperty] private string _animation = DefaultAnimation;

    /// <summary>The move part of <see cref="Animation"/> ("JumpOff" in "JumpOff|18").</summary>
    public string MoveName
    {
        get => Split().Move;
        set
        {
            // A dropdown losing its item (moves not loaded yet) pushes null: keep the move.
            if (!string.IsNullOrWhiteSpace(value))
                Animation = Compose(value, StartFrame, Reverse);
        }
    }

    /// <summary>The start-frame part of <see cref="Animation"/> (18 in "JumpOff|18").</summary>
    public int StartFrame
    {
        get => Split().Frame;
        set => Animation = Compose(MoveName, Math.Max(0, value), Reverse);
    }

    /// <summary>
    /// The optional third part of <see cref="Animation"/>: the game plays the spawn's move reversed (to the left) when
    /// it is above 0 ("JumpOff|18|1").
    /// </summary>
    public bool Reverse
    {
        get => Split().Reverse;
        set
        {
            if (value != Reverse)
                Animation = Compose(MoveName, StartFrame, value);
        }
    }

    private (string Move, int Frame, bool Reverse) Split()
    {
        var parts = (Animation ?? string.Empty).Split('|');
        string move = parts[0].Trim();
        int frame = 0;
        if (parts.Length > 1)
            int.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out frame);
        bool reverse = parts.Length > 2 && int.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int r) && r > 0;
        return (move, Math.Max(0, frame), reverse);
    }

    private static string Compose(string? move, int frame, bool reverse)
        => (move ?? string.Empty).Trim() + "|" + frame.ToString(System.Globalization.CultureInfo.InvariantCulture) + (reverse ? "|1" : string.Empty);

    partial void OnAnimationChanged(string value)
    {
        OnPropertyChanged(nameof(MoveName));
        OnPropertyChanged(nameof(StartFrame));
        OnPropertyChanged(nameof(Reverse));
    }

    protected override SceneElement CreateEmpty() => new SpawnElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        ((SpawnElement)target).Animation = Animation;
    }
}
