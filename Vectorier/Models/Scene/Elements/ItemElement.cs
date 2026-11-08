using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

public enum ItemType
{
    Bonus = 0,
    Coin = 1
}

/// <summary>&lt;Item&gt;: a collectible (Bonus pill or Coin).</summary>
public sealed partial class ItemElement : PointElement
{
    public override ElementType Type => ElementType.Item;
    public override bool NameEditable => false;

    [ObservableProperty] private ItemType _itemType = ItemType.Bonus;
    [ObservableProperty] private int _score = 10;
    [ObservableProperty] private double _radius = 80;
    [ObservableProperty] private int _groupId = 1;

    partial void OnItemTypeChanged(ItemType value) => Name = value == ItemType.Bonus ? "Bonus" : "Coin";

    protected override SceneElement CreateEmpty() => new ItemElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (ItemElement)target;
        t.ItemType = ItemType;
        t.Score = Score;
        t.Radius = Radius;
        t.GroupId = GroupId;
        t.Name = Name;
    }
}
