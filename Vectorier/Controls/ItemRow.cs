using System.Collections.Generic;
using System.Linq;

namespace Vectorier.Controls;

/// <summary>
/// One line of a tile grid. Grids are split into rows shown by a virtualizing list, so only the rows on screen
/// (and their images) are created, however many items there are.
/// </summary>
public sealed class ItemRow
{
    public ItemRow(IReadOnlyList<object> items) => Items = items;

    public IReadOnlyList<object> Items { get; }

    public static List<ItemRow> Chunk<T>(IEnumerable<T> items, int perRow) where T : class
    {
        var rows = new List<ItemRow>();
        var list = items.Cast<object>().ToList();
        perRow = System.Math.Max(1, perRow);
        for (int i = 0; i < list.Count; i += perRow)
            rows.Add(new ItemRow(list.GetRange(i, System.Math.Min(perRow, list.Count - i))));
        return rows;
    }
}
