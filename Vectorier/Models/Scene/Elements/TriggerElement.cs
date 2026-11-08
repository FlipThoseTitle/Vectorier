using System;
using System.Collections.Generic;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

/// <summary>&lt;Trigger&gt;: a rectangle whose Content holds game logic.</summary>
public sealed partial class TriggerElement : SceneElement
{
    public override ElementType Type => ElementType.Trigger;
    public override bool LayerEditable => false; // always on the track layer

    /// <summary>Inner XML of &lt;Content&gt; (Init, Loop, Template...).</summary>
    [ObservableProperty] private string _contentXml = string.Empty;

    /// <summary>
    /// Editor-only: the type of each variable in the Trigger Editor (Unity's TriggerComponent.variableTypes).
    /// Never exported; it only changes which value editor is shown.
    /// </summary>
    public Dictionary<string, string> VariableTypes { get; set; } = new();

    /// <summary>
    /// Key of this trigger in the editor cache of variable types: its name plus a hash of its content with
    /// formatting removed, so the same trigger is found again after saving, reopening, copying or storing it.
    /// </summary>
    public string TypeCacheKey => Name + "@" + ContentHash(ContentXml);

    public static string ContentHash(string content)
    {
        string canonical;
        try
        {
            var root = XElement.Parse("<C>" + content + "</C>", LoadOptions.None);
            canonical = root.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            canonical = string.Join(" ", (content ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        // FNV-1a 64-bit (stable across runs and platforms, no crypto needed in the browser).
        ulong hash = 14695981039346656037UL;
        foreach (char ch in canonical)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }

    protected override SceneElement CreateEmpty() => new TriggerElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (TriggerElement)target;
        t.ContentXml = ContentXml;
        t.VariableTypes = new Dictionary<string, string>(VariableTypes);
    }
}
