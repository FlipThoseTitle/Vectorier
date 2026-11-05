using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// Play Here (Scene context menu): the level is saved as usual, then played in the Game View from the clicked point:
/// the game gets its own copy of the level's XML, changed so the player starts there. Nothing of it reaches the
/// editor's level or the file on disk.
/// </summary>
public partial class EditorViewModel
{
    public async Task PlayHereAsync(Point level)
    {
        if (!CanPlay)
            return;
        CommitPending();
        string levelName = Document.LevelName;
        string target = VPath.Combine(Project.LevelsFolder, levelName + ".xml");
        byte[] run;
        try
        {
            // 1. Save, as Play does.
            byte[] saved = ExportXml(out _);
            await AppServices.FileSystem.CreateDirectoryAsync(Project.LevelsFolder);
            await AppServices.FileSystem.WriteAllBytesAsync(target, saved);
            if (string.Equals(Document.FilePath, target, StringComparison.Ordinal))
                MarkSaved();

            // 2. The run's own version of the level (in memory only).
            XDocument xml;
            using (var stream = new MemoryStream(saved))
                xml = XDocument.Load(stream);
            PlayHereXml.Apply(xml, level.X, level.Y);
            run = XmlHelpers.ToBytes(xml);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Play Here"), ex.Message, MessageKind.Error);
            return;
        }

        if (await StartGameAsync(levelName, run, false, Loc.T("Play Here")))
            StatusText = Loc.F("Playing {0} from the clicked point", levelName);
    }
}

/// <summary>The changes Play Here makes to a level's XML for one run.</summary>
public static class PlayHereXml
{
    public const string SpawnName = "_PlayHereSpawn";
    public const string TriggerName = "_PlayHereCamera";

    // Large enough to hold the runner's COM wherever it appears around the spawn.
    private const double TriggerWidth = 800;
    private const double TriggerHeight = 1000;

    /// <summary>
    /// Removes the level's cameras; adds, at (x, y) on the main layer, a spawn, a camera and a trigger making the
    /// camera follow the player (AI 0) once it is inside; and starts the player's models at that spawn.
    /// </summary>
    public static void Apply(XDocument xml, double x, double y)
    {
        var root = xml.Root ?? throw new InvalidDataException("The level has no root element.");
        var track = root.Element("Track");
        if (track == null)
        {
            track = new XElement("Track");
            root.Add(track);
        }

        foreach (var camera in track.Descendants("Camera").ToList())
            camera.Remove();

        var content = MainLayerContent(track);
        content.Add(new XElement("Spawn",
            new XAttribute("X", Num.Format(x)),
            new XAttribute("Y", Num.Format(y)),
            new XAttribute("Name", SpawnName),
            new XAttribute("Animation", SpawnElement.DefaultAnimation)));
        content.Add(new XElement("Camera",
            new XAttribute("X", Num.Format(x)),
            new XAttribute("Y", Num.Format(y))));
        content.Add(new XElement("Trigger",
            new XAttribute("Name", TriggerName),
            new XAttribute("X", Num.Format(x - TriggerWidth / 2)),
            new XAttribute("Y", Num.Format(y - TriggerHeight / 2)),
            new XAttribute("Width", Num.Format(TriggerWidth)),
            new XAttribute("Height", Num.Format(TriggerHeight)),
            new XElement("Content",
                new XElement("Init",
                    SetVariable("$Active", "1"),
                    SetVariable("$AI", "0"),
                    SetVariable("$Node", "COM")),
                new XElement("Loop",
                    new XElement("Events", new XElement("Enter")),
                    new XElement("Actions", new XElement("Camera", new XAttribute("Follow", "_$Model")))))));

        // The player (AI 0) starts at the new spawn, in every mode's model list.
        foreach (var model in root.Elements("Models").Elements("Model"))
        {
            if ((string?)model.Attribute("AI") == "0")
                model.SetAttributeValue("BirthSpawn", SpawnName);
        }
    }

    private static XElement SetVariable(string name, string value) =>
        new("SetVariable", new XAttribute("Name", name), new XAttribute("Value", value));

    /// <summary>The Content of the Track's Factor 1 layer (created when the level has none).</summary>
    private static XElement MainLayerContent(XElement track)
    {
        var layers = track.Elements("Object").ToList();
        var main = layers.FirstOrDefault(o => Math.Abs(Num.Parse((string?)o.Attribute("Factor"), double.NaN) - 1) < 1e-9);
        if (main == null)
        {
            main = new XElement("Object", new XAttribute("Factor", "1"));
            // Layers are written from the farthest (smallest factor) to the nearest.
            var after = layers.LastOrDefault(o => Num.Parse((string?)o.Attribute("Factor"), 1) < 1);
            if (after != null)
                after.AddAfterSelf(main);
            else if (layers.Count > 0)
                layers[0].AddBeforeSelf(main);
            else
                track.Add(main);
        }
        var content = main.Element("Content");
        if (content == null)
        {
            content = new XElement("Content");
            main.Add(content);
        }
        return content;
    }
}
