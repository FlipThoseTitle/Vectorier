using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
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
