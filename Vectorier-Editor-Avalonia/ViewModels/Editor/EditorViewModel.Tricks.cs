using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Xml;

namespace Vectorier_Editor_Avalonia.ViewModels.Editor;

/// <summary>
/// Trick Area names. A Trick Area is named after its trick's trigger: the trick's ItemName (TRICK_...) is found
/// on a move in &lt;Moves&gt;, and the entry of the TrickGroup reaction group with that move's element name gives
/// the TriggerName (e.g. TRICK_REVERSEVAULT → &lt;ReverseVaultStart&gt; → TriggerReverseVault). Unknown tricks
/// fall back to "Area".
/// </summary>
public sealed partial class EditorViewModel
{
    public const string DefaultAreaName = "Area";

    private Dictionary<string, string>? _trickTriggers;
    private Task? _trickTriggersLoad;

    /// <summary>ItemName → TriggerName from moves.xml (empty when the file is missing or invalid).</summary>
    public static Dictionary<string, string> ReadTrickTriggers(XDocument doc)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = doc.Root;
        if (root == null)
            return map;
        var group = root.Element("ReactionGroups")?.Elements("ReactionGroup")
            .FirstOrDefault(g => string.Equals((string?)g.Attribute("Name"), "TrickGroup", StringComparison.OrdinalIgnoreCase));
        if (group == null)
            return map;
        var triggers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in group.Elements())
        {
            if ((string?)entry.Attribute("TriggerName") is { Length: > 0 } trigger)
                triggers.TryAdd(entry.Name.LocalName, trigger);
        }
        foreach (var move in root.Element("Moves")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if ((string?)move.Attribute("ItemName") is { Length: > 0 } item && triggers.TryGetValue(move.Name.LocalName, out var trigger))
                map.TryAdd(item, trigger);
        }
        return map;
    }

    private Task EnsureTrickTriggersAsync() => _trickTriggersLoad ??= LoadTrickTriggersAsync();

    private async Task LoadTrickTriggersAsync()
    {
        try
        {
            string path = VPath.Combine(Project.AnimationsFolder, "moves.xml");
            if (await AppServices.FileSystem.FileExistsAsync(path))
            {
                byte[] data = await AppServices.FileSystem.ReadAllBytesAsync(path);
                _trickTriggers = await Task.Run(() => ReadTrickTriggers(XmlHelpers.ParseDocument(data)));
                return;
            }
        }
        catch
        {
            // fall through: no names
        }
        _trickTriggers = new Dictionary<string, string>();
    }

    /// <summary>The name a Trick Area gets for a trick.</summary>
    public string TrickAreaName(string? itemName) =>
        itemName != null && _trickTriggers != null && _trickTriggers.TryGetValue(itemName, out var trigger) ? trigger : DefaultAreaName;

    /// <summary>Called when an Area's type or trick changes: a Trick Area takes its trick's trigger name.</summary>
    private void OnAreaPropertyChanged(SceneElement element, string? property)
    {
        if (element is not AreaElement { IsTrick: true } area || property is not (nameof(AreaElement.ItemName) or nameof(AreaElement.AreaType)))
            return;
        if (_trickTriggers != null)
        {
            ApplyTrickName(area);
            return;
        }
        _ = EnsureTrickTriggersAsync().ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTrickName(area)), TaskScheduler.Default);
    }

    private void ApplyTrickName(AreaElement area)
    {
        if (!area.IsTrick)
            return;
        string name = TrickAreaName(area.ItemName);
        if (area.Name != name)
            area.Name = name;
    }
}
