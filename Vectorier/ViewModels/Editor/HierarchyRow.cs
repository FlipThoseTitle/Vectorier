using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Models.Scene;

namespace Vectorier.ViewModels.Editor;

/// <summary>A visible line of the Hierarchy (the tree is flattened so long levels stay fast).</summary>
public sealed class HierarchyRow : ObservableObject
{
    public HierarchyRow(SceneElement? element, int depth, string? rootName = null)
    {
        Element = element;
        Depth = depth;
        RootName = rootName;
        HasChildren = ComputeHasChildren();
        IsReference = Element is ObjectElement { IsReference: true };
    }

    /// <summary>Null for the root line showing the level name.</summary>
    public SceneElement? Element { get; }

    public string? RootName { get; }

    public bool IsRoot => Element == null;

    public int Depth { get; }

    public Avalonia.Thickness Indent => new(4 + Depth * 14, 0, 0, 0);

    public bool HasChildren { get; private set; }

    public bool IsReference { get; private set; }

    public StreamGeometry? Icon => Element == null ? ElementTypeInfo.GetIcon(ElementType.Object) : ElementTypeInfo.GetIcon(Element.Type);

    private bool ComputeHasChildren() => Element is ObjectElement obj && obj.Children.Count > 0;

    /// <summary>A reused row picks up changes of its element (children added, reference resolved).</summary>
    internal void Refresh()
    {
        bool has = ComputeHasChildren();
        if (has != HasChildren)
        {
            HasChildren = has;
            OnPropertyChanged(nameof(HasChildren));
        }
        bool reference = Element is ObjectElement { IsReference: true };
        if (reference != IsReference)
        {
            IsReference = reference;
            OnPropertyChanged(nameof(IsReference));
        }
    }
}

public enum HierarchyDropPosition
{
    Before,
    Inside,
    After
}
