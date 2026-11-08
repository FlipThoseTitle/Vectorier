using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

public partial class ProjectWindowViewModel
{
    private readonly Dictionary<PlatformSurfaceType, ObjectDefinition> _platformPreviews = new();

    /// <summary>A 500 × 500 platform of the surface type, for the grid and the placement preview.</summary>
    private async Task<ObjectDefinition> PlatformPreviewAsync(PlatformSurfaceType surface)
    {
        if (_platformPreviews.TryGetValue(surface, out var cached))
            return cached;
        var group = await PlatformSurface.BuildAsync(Textures, surface, new Rect(0, 0, 500, 500), 12345);
        var def = new ObjectDefinition(surface.ToString(), string.Empty, new XElement("Object"))
        {
            Content = group.Children.ToList(),
            IsBuilt = true,
        };
        def.ComputeBounds();
        _platformPreviews[surface] = def;
        return def;
    }

    // ================= OBJECT FILES: TYPE, RENAME, EDIT ================= //

    private async Task DetectFileTypesAsync(IEnumerable<PwFolder> folders)
    {
        foreach (var folder in folders.Where(f => f.FileName != null).ToList())
        {
            try { folder.IsBuilding = await Assets.Objects.IsBuildingFileAsync(folder.FileName!); }
            catch { folder.IsBuilding = false; }
        }
    }

    /// <summary>
    /// Object ↔ Building: Building writes In / Out (and Box) on every definition of the file, computed from its
    /// Platforms; Object removes them.
    /// </summary>
    public async Task SetFileTypeAsync(PwFolder folder, bool building)
    {
        if (folder.FileName is not { } file || folder.IsBuilding == building)
            return;
        try
        {
            string path = VPath.Combine(_editor.Project.XmlRoot, file);
            var doc = await ObjectFileOps.ReadAsync(path);
            var definitions = await Assets.Objects.GetFileDefinitionsAsync(file);
            foreach (var xml in doc.Root?.Element("Objects")?.Elements("Object") ?? Enumerable.Empty<XElement>())
            {
                if (!building)
                {
                    ObjectFileOps.RemoveBuildingAttributes(xml);
                    continue;
                }
                var def = definitions.FirstOrDefault(d => d.Name == (string?)xml.Attribute("Name"));
                if (def == null)
                    continue;
                _editor.BuildDefinition(def);
                var (inPt, outPt, box) = ObjectFileOps.ComputeBuilding(def.Content, def.In, def.Out);
                ObjectFileOps.WriteBuildingAttributes(xml, inPt, outPt, box);
            }
            await ObjectFileOps.WriteAsync(path, doc);
            Assets.Objects.Invalidate(file);
            folder.IsBuilding = building;
            _editor.StatusText = Loc.F("{0} is now {1} file", file, (building ? "a Building" : "an Object"));
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Object XML Type"), ex.Message, MessageKind.Error);
        }
    }

    /// <summary>Double click / Edit Object on a definition: opens it in the Object tab.</summary>
    public async Task EditObjectAsync(PwItem item)
    {
        if (item.PlatformSurface == null && item.Definition is { } def)
            await _editor.EnterObjectEditAsync(def);
    }

    /// <summary>
    /// Renames a definition and every reference to it in the project: its file, the Object XML files and the levels
    /// whose Sets list that file. Asks first; an existing name is refused.
    /// </summary>
    public async Task RenameDefinitionAsync(PwItem item)
    {
        if (item.Definition is not { } def)
            return;
        var allNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in await ObjectFilesAsync())
            foreach (var d in await Assets.Objects.GetFileDefinitionsAsync(file))
                allNames.Add(d.Name);

        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Rename Object"), Loc.F("New name for '{0}' ({1}):", def.Name, def.SourceFile), def.Name,
            text => string.IsNullOrWhiteSpace(text) ? Loc.T("Enter a name.") : null);
        if (name == null || (name = name.Trim()) == def.Name)
            return;
        if (allNames.Contains(name))
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Rename Object"), Loc.F("An object named '{0}' already exists. The name was not changed.", name), MessageKind.Warning);
            return;
        }
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Rename Object"),
                Loc.F("Rename '{0}' to '{1}'?\n\nEvery reference in the Object XML files and levels that use {2} is updated too. This can take a moment.", def.Name, name, def.SourceFile),
                Loc.T("Rename"), Loc.T("Cancel"), MessageKind.Question))
            return;
        try
        {
            IsBusy = true;
            string old = def.Name;
            var report = await ObjectFileOps.RenameDefinitionAsync(_editor.Project, def.SourceFile, old, name);
            Assets.Objects.Invalidate();
            await _editor.OnDefinitionRenamedAsync(old, name);
            await ReloadAsync();
            _editor.StatusText = Loc.F("Renamed {0} to {1} in {2} file(s)", old, name, report.ChangedFiles.Count);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Rename Object"), ex.Message, MessageKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
