using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Platform.Storage;

namespace Vectorier.Models.Scene;

/// <summary>
/// The working representation of an opened level. The Hierarchy, Scene and Inspector all read and edit
/// this object; XML is only produced when saving.
/// </summary>
public sealed class SceneDocument
{
    /// <summary>Level name without extension, e.g. DOWNTOWN_STORY_02.</summary>
    public string LevelName { get; set; } = "New_Level";

    public LevelSettings Settings { get; set; } = new();

    /// <summary>Top-level elements in hierarchy order. Each carries its own layer.</summary>
    public ObservableCollection<SceneElement> Roots { get; } = new();

    /// <summary>Relative path of the XML inside the Vectorier folder, or null when not saved yet.</summary>
    public string? FilePath { get; set; }

    /// <summary>File outside the Vectorier folder (opened through a picker).</summary>
    public IStorageFile? ExternalFile { get; set; }

    public bool HasLocation => FilePath != null || ExternalFile != null;

    /// <summary>All elements, depth-first, in hierarchy order (inline content only).</summary>
    public IEnumerable<SceneElement> AllElements()
    {
        foreach (var root in Roots)
        {
            foreach (var e in Walk(root))
                yield return e;
        }
    }

    public static IEnumerable<SceneElement> Walk(SceneElement element)
    {
        yield return element;
        if (element is ObjectElement obj)
        {
            foreach (var child in obj.Children)
            {
                foreach (var e in Walk(child))
                    yield return e;
            }
        }
    }

    /// <summary>Siblings list that contains the element (the parent's children, or the roots).</summary>
    public IList<SceneElement> SiblingsOf(SceneElement element)
        => element.Parent != null ? element.Parent.Children : Roots;

    public void Insert(SceneElement element, ObjectElement? parent, int index = -1)
    {
        if (parent != null)
        {
            parent.AddChild(element, index);
            return;
        }

        element.Parent = null;
        if (index < 0 || index > Roots.Count)
            Roots.Add(element);
        else
            Roots.Insert(index, element);
    }

    public void Remove(SceneElement element)
    {
        if (element.Parent != null)
            element.Parent.RemoveChild(element);
        else
            Roots.Remove(element);
    }

    public SceneElement? FindById(int id) => AllElements().FirstOrDefault(e => e.Id == id);

    // ================= UNDO SNAPSHOTS ================= //

    public sealed record Snapshot(List<SceneElement> Roots, LevelSettings Settings);

    public Snapshot CreateSnapshot()
        => new(Roots.Select(ObjectElement.CloneKeepingIds).ToList(), Settings.Clone());

    public void RestoreSnapshot(Snapshot snapshot)
    {
        Roots.Clear();
        foreach (var root in snapshot.Roots)
        {
            var copy = ObjectElement.CloneKeepingIds(root);
            copy.Parent = null;
            Roots.Add(copy);
        }
        Settings = snapshot.Settings.Clone();
    }
}
