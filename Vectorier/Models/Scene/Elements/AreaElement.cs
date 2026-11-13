using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

/// <summary>&lt;Area&gt;: animation / catch / trick / help regions.</summary>
public sealed partial class AreaElement : SceneElement
{
    public static readonly string[] KnownTypes = { "Animation", "Catch", "Trick", "Help" };

    public override ElementType Type => ElementType.Area;
    public override bool LayerEditable => false; // always on the track layer

    [ObservableProperty] private string _areaType = "Animation";
    [ObservableProperty] private int _distance = 300;
    [ObservableProperty] private string _itemName = "TRICK_";
    [ObservableProperty] private int _score = 100;
    [ObservableProperty] private string _key = "Up";
    [ObservableProperty] private string _description = string.Empty;

    /// <summary>Trick Areas always have the game's trick size.</summary>
    public const double TrickWidth = 150, TrickHeight = 200;

    public bool IsTrick => AreaType == "Trick";

    /// <summary>
    /// Trick Areas have a fixed size. Their name is set from the trick when it (or the type) changes, and can then
    /// be edited like any other name.
    /// </summary>
    public override bool CanResize => !IsTrick;

    partial void OnAreaTypeChanged(string value)
    {
        if (value == "Trick")
        {
            Width = TrickWidth;
            Height = TrickHeight;
        }
        OnPropertyChanged(nameof(IsTrick));
    }

    public override void ScaleAround(Point pivot, double sx, double sy)
    {
        if (!IsTrick)
        {
            base.ScaleAround(pivot, sx, sy);
            return;
        }
        // Only the position follows (the size stays 150 × 200).
        X = pivot.X + (X - pivot.X) * sx;
        Y = pivot.Y + (Y - pivot.Y) * sy;
    }

    protected override SceneElement CreateEmpty() => new AreaElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (AreaElement)target;
        t.AreaType = AreaType;
        t.Distance = Distance;
        t.ItemName = ItemName;
        t.Score = Score;
        t.Key = Key;
        t.Description = Description;
    }
}
