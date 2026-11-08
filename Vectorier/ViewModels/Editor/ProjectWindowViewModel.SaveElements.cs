using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

public partial class ProjectWindowViewModel
{
    /// <summary>The elements of one kind among those dragged from the Hierarchy, including inside dragged groups.</summary>
    private static List<T> Dragged<T>(IReadOnlyList<SceneElement> elements) where T : SceneElement =>
        elements.SelectMany(SceneDocument.Walk).OfType<T>().Distinct().ToList();

    public bool CanAcceptElements(IReadOnlyList<SceneElement> elements) => Context switch
    {
        PwContext.Objects => SelectedFolder?.FileName != null && elements.Count > 0,
        PwContext.Triggers => Dragged<TriggerElement>(elements).Count > 0,
        PwContext.Areas => Dragged<AreaElement>(elements).Count > 0,
        _ => false,
    };

    public async Task AcceptElementsAsync(IReadOnlyList<SceneElement> elements)
    {
        try
        {
            switch (Context)
            {
                case PwContext.Objects when SelectedFolder?.FileName is { } file:
                    {
                        var saved = await SaveDefinitionsAsync(file, elements);
                        // A saved Object without definition becomes an instance of its new definition (as do others of that name).
                        foreach (var (element, name) in saved)
                            if (element is ObjectElement { IsReference: false } obj && obj.Name != name)
                                obj.Name = name;
                        await _editor.RefreshDefinitionsAsync(new[] { file });
                        await LoadItemsAsync();
                        await RevealNewAsync(saved.Select(x => x.Name).ToList(), file);
                        return;
                    }
                case PwContext.Triggers:
                    {
                        // Every trigger dragged (also those inside dragged groups) is stored on its own.
                        var ids = new List<string>();
                        foreach (var trigger in Dragged<TriggerElement>(elements))
                        {
                            var stored = new StoredTrigger { Name = trigger.Name, Width = trigger.Width, Height = trigger.Height, Content = trigger.ContentXml };
                            Cache.Data.Triggers.Add(stored);
                            if (SelectedFolder is { IsUserFolder: true } folder)
                                Cache.AssignToFolder(TriggersKey, stored.Id, folder.Name);
                            ids.Add(stored.Id);
                        }
                        if (ids.Count == 0)
                            return;
                        await Cache.SaveAsync();
                        _editor.StatusText = ids.Count == 1 ? Loc.F("Stored Trigger '{0}'", Cache.Data.Triggers.First(t => t.Id == ids[0]).Name) : Loc.F("Stored {0} Triggers", ids.Count);
                        await RevealNewAsync(ids);
                        return;
                    }
                case PwContext.Areas:
                    {
                        var ids = new List<string>();
                        foreach (var area in Dragged<AreaElement>(elements))
                        {
                            var stored = new StoredArea
                            {
                                Name = area.Name,
                                Width = area.Width,
                                Height = area.Height,
                                AreaType = area.AreaType,
                                Distance = area.Distance,
                                ItemName = area.ItemName,
                                Score = area.Score,
                                Key = area.Key,
                                Description = area.Description,
                            };
                            Cache.Data.Areas.Add(stored);
                            if (SelectedFolder is { IsUserFolder: true } folder)
                                Cache.AssignToFolder(AreasKey, stored.Id, folder.Name);
                            ids.Add(stored.Id);
                        }
                        if (ids.Count == 0)
                            return;
                        await Cache.SaveAsync();
                        _editor.StatusText = ids.Count == 1 ? Loc.F("Stored Area '{0}'", Cache.Data.Areas.First(a => a.Id == ids[0]).Name) : Loc.F("Stored {0} Areas", ids.Count);
                        await RevealNewAsync(ids);
                        return;
                    }
                default:
                    return;
            }
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Save to Project"), ex.Message, MessageKind.Error);
        }
    }

    private async Task<XDocument> ReadXmlRootFileAsync(string path)
    {
        if (!await AppServices.FileSystem.FileExistsAsync(path))
            return new XDocument(new XElement("Root", new XElement("Objects")));
        return XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(path));
    }

    /// <summary>
    /// Saves elements as object definitions. Objects are saved as they are; other elements get an Object
    /// parent. Names already used in the file get a "_1" suffix.
    /// </summary>
    private async Task<List<(SceneElement Element, string Name)>> SaveDefinitionsAsync(string file, IReadOnlyList<SceneElement> elements)
    {
        string path = VPath.Combine(_editor.Project.XmlRoot, file);
        var doc = await ReadXmlRootFileAsync(path);
        var root = doc.Root ?? new XElement("Root");
        var objects = root.Element("Objects");
        if (objects == null)
        {
            objects = new XElement("Objects");
            root.Add(objects);
        }
        var used = objects.Elements("Object").Select(o => (string?)o.Attribute("Name")).Where(n => n != null).Select(n => n!).ToHashSet(StringComparer.Ordinal);

        var writer = new SceneXmlWriter
        {
            TextureSizeProvider = name => Textures.TryGetSize(name),
        };

        var saved = new List<string>();
        var result = new List<(SceneElement, string)>();
        foreach (var element in elements)
        {
            string baseName = string.IsNullOrEmpty(element.Name) ? element.Type.ToString() : element.Name;
            string name = baseName;
            for (int i = 1; used.Contains(name); i++)
                name = $"{baseName}_{i}";
            used.Add(name);

            XElement definition;
            if (element is ObjectElement obj)
            {
                var copy = (ObjectElement)obj.Clone(keepIds: false);
                copy.Name = name;
                copy.X = 0;
                copy.Y = 0;
                copy.PositionWasPresent = false;
                definition = (XElement)writer.WriteElement(copy)!;
                // A definition keeps the layer-independent content; a referenced prefab keeps its own content.
                if (obj.Children.Count == 0 && obj.Reference != null && definition.Element("Content") == null)
                {
                    var content = new XElement("Content");
                    var reference = new XElement("Object", new XAttribute("Name", obj.Reference.Name));
                    content.Add(reference);
                    definition.Add(content);
                }
            }
            else
            {
                var copy = element.Clone(keepIds: false);
                // The content's origin is the element itself.
                copy.X = 0;
                copy.Y = 0;
                definition = writer.WriteDefinition(name, new[] { copy });
            }
            objects.Add(definition);
            saved.Add(name);
            result.Add((element, name));
        }

        await AppServices.FileSystem.WriteAllBytesAsync(path, XmlHelpers.ToBytes(doc));
        Assets.Objects.Invalidate(file);
        _editor.StatusText = Loc.F("Saved {0} to {1}", string.Join(", ", saved), file);
        if (writer.Errors.Count > 0)
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Save to Project"), string.Join("\n", writer.Errors), MessageKind.Warning);
        return result;
    }

    private async Task RemoveDefinitionsAsync(string file, IReadOnlyCollection<string> names)
    {
        string path = VPath.Combine(_editor.Project.XmlRoot, file);
        var doc = await ReadXmlRootFileAsync(path);
        foreach (var xml in doc.Root?.Element("Objects")?.Elements("Object").Where(o => names.Contains((string?)o.Attribute("Name") ?? string.Empty)).ToList()
                     ?? new List<XElement>())
            xml.Remove();
        await AppServices.FileSystem.WriteAllBytesAsync(path, XmlHelpers.ToBytes(doc));
        Assets.Objects.Invalidate(file);
    }
}
