using Avalonia;
using Vectorier.Models.Scene;

namespace Vectorier.Controls.Scene;

/// <summary>A drawable piece of the scene: one element, or one element of a referenced object's content.</summary>
public sealed class RenderItem
{
    /// <summary>The element that is drawn.</summary>
    public SceneElement Element = null!;

    /// <summary>The element of the level that is selected when this item is clicked.</summary>
    public SceneElement Owner = null!;

    /// <summary>For content resolved from &lt;Sets&gt;: the level Object whose definition this belongs to.</summary>
    public ObjectElement? Instance;

    /// <summary>For resolved content: position of the element's parent inside the definition, relative to the instance.</summary>
    public Vector GhostOffset;

    public LayerGroup Group = null!;
    public bool IsOverlay;
    public long SortKey;

    /// <summary>Everything the item can draw (image, outline, gizmo...), in the level space of its layer.</summary>
    public Rect Bounds;

    public bool Dirty = true;

    // Spatial grid bookkeeping.
    internal bool InGrid;
    internal bool Large;
    internal int CellX0, CellY0, CellX1, CellY1;
    internal int Stamp;
}
