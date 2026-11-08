using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.MoveVisualizer;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

public partial class MoveVisualizerViewModel
{
    private async Task<bool> PlaceAtAsync(Vector3 space, SceneElement? host)
    {
        var model = await EnsureModelAsync();
        if (model == null) return false;
        var bin = await ResolvePlacementBinAsync();
        if (bin == null) return false;
        return Place(space, host, model, bin, PivotNodeName, Animation.StartFrame);
    }

    private bool Place(Vector3 space, SceneElement? host, MoveModel model, byte[] bin, string pivot, int startFrame, bool? reverse = null)
    {
        try
        {
            Animation.Reverse = reverse ?? Reverse;
            Animation.StartFrame = startFrame;
            Animation.PlaceAt(space, model, bin, pivot, false, Now);
            Host = host;
            _lastPlacedPosition = space;
            IsPlaced = true;
            // The start pose, its cloth already hanging.
            model.Placed.ResetToRest();
            SyncPlacedModel(tick: false);
            model.Placed.Settle(60);
            StatusMessage = string.Empty;
            SyncFrames();
            UpdateTimer();
            Follow();
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Report(ex.Message);
            return false;
        }
    }

    private Task<byte[]?> ResolvePlacementBinAsync() => ReadAnyAsync(VPath.Combine(MovementFolder, MovementBins[MovementType]));

    /// <summary>A path inside the Vectorier folder, or (desktop) an absolute path.</summary>
    private async Task<byte[]?> ReadAnyAsync(string path)
    {
        try
        {
            var fs = AppServices.FileSystem;
            if (!Path.IsPathRooted(path) && await fs.FileExistsAsync(path))
                return await fs.ReadAllBytesAsync(path);
            if (Path.IsPathRooted(path) && !OperatingSystem.IsBrowser() && File.Exists(path))
                return await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex)
        {
            Report(ex.Message);
            return null;
        }
        Report(Loc.T("Bin file not found: ") + path);
        return null;
    }

    /// <summary>Unity ActiveProject.ResolveFile: the active project, then the Vector project.</summary>
    private async Task<string?> ResolveProjectFileAsync(string relative)
    {
        var fs = AppServices.FileSystem;
        string path = VPath.Combine(_editor.Project.Path, relative);
        if (await fs.FileExistsAsync(path)) return path;
        string fallback = VPath.Combine("Projects", DefaultProject, relative);
        return await fs.FileExistsAsync(fallback) ? fallback : null;
    }

    private async Task<XElement?> LoadMovesRootAsync()
    {
        string? path = await ResolveProjectFileAsync(MovesRelativePath);
        if (path == null)
            throw new FileNotFoundException("moves.xml not found: " + VPath.Combine(_editor.Project.Path, MovesRelativePath));
        var doc = XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(path));
        return doc.Root ?? throw new InvalidDataException("moves.xml has no root element.");
    }

    private static XElement? FindMove(XElement root, string name) =>
        root.Element("Moves")?.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

    private static XElement? TrickGroup(XElement root) =>
        root.Element("ReactionGroups")?.Elements("ReactionGroup")
            .FirstOrDefault(g => string.Equals((string?)g.Attribute("Name"), "TrickGroup", StringComparison.OrdinalIgnoreCase));

    // ---------------- Spawn ----------------

    /// <summary>Spawn "Preview Animation": plays the spawn's "Move|Frame" at the spawn point.</summary>
    public async Task PreviewSpawnAsync(SpawnElement spawn)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(spawn.Animation))
                throw new InvalidDataException("Animation string is empty.");
            string[] parts = spawn.Animation.Split('|');
            string animationName = parts[0];
            int startFrame = 0;
            if (parts.Length > 1) int.TryParse(parts[1], out startFrame);

            string pivot = "NPivot";
            byte[]? bin;
            // JumpOff and Run come from the editor's own animations folder (Unity overrides).
            if (animationName.Equals("JumpOff", StringComparison.OrdinalIgnoreCase))
            {
                bin = await ReadAnyAsync(VPath.Combine(MovementFolder, "jump_off_fly.bin"));
                startFrame = 0;
            }
            else if (animationName.Equals("Run", StringComparison.OrdinalIgnoreCase))
            {
                bin = await ReadAnyAsync(VPath.Combine(MovementFolder, "run.bin"));
                startFrame = 0;
            }
            else
            {
                var root = await LoadMovesRootAsync();
                var move = FindMove(root!, animationName) ?? throw new InvalidDataException($"Could not find <Moves><{animationName} ... /> in moves.xml.");
                string? fileName = (string?)move.Attribute("FileName");
                if (string.IsNullOrWhiteSpace(fileName))
                    throw new InvalidDataException($"Move '{animationName}' does not have a FileName attribute.");
                if ((string?)move.Attribute("PivotNode") is { Length: > 0 } p && !string.IsNullOrWhiteSpace(p))
                    pivot = p;
                string? path = await ResolveProjectFileAsync($"{BinsRelativeFolder}/{fileName}")
                               ?? throw new FileNotFoundException("Animation bin not found: " + VPath.Combine(_editor.Project.Path, BinsRelativeFolder, fileName));
                bin = await AppServices.FileSystem.ReadAllBytesAsync(path);
            }
            if (bin == null) return;

            var model = await EnsureModelAsync();
            if (model == null) return;
            StopPlacement();
            var host = (SceneElement?)spawn.Parent ?? spawn;
            if (SpawnMode != MoveSpawnMode.Animation)
                SpawnMode = MoveSpawnMode.Animation;
            if (Place(ToSpace(spawn.WorldPosition, host), host, model, bin, pivot, startFrame, spawn.Reverse))
                PivotNodeName = pivot;
        }
        catch (Exception ex)
        {
            Report(Loc.F("Preview Animation failed for '{0}': {1}", spawn.Name, ex.Message));
        }
    }

    // ---------------- Trigger ----------------

    /// <summary>The &lt;ForceAnimation&gt; actions the trigger runs (its own loops and its template's).</summary>
    public async Task<List<Services.Triggers.TriggerForcedAnimation>> FindTriggerAnimationsAsync(TriggerElement trigger)
    {
        XElement? templates = null;
        try
        {
            string path = VPath.Combine(_editor.Project.XmlRoot, "TriggerTemplates.xml");
            if (await AppServices.FileSystem.FileExistsAsync(path))
                templates = XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(path)).Root;
        }
        catch
        {
            // without templates: only the trigger's own loops
        }
        string content = trigger.ContentXml;
        return await Task.Run(() => Services.Triggers.TriggerAnimationResolver.Find(content, templates));
    }

    /// <summary>
    /// Trigger "Preview Animation": plays the move of the trigger's Force Animation where the model meets the
    /// trigger. Model Enters the Trigger (and any other event): the COM on the trigger's left side, DetectorH on
    /// its bottom; Model Leaves the Trigger: the COM on its right side. The preview follows the trigger when it moves.
    /// </summary>
    public async Task PreviewTriggerAsync(TriggerElement trigger)
    {
        try
        {
            var animations = await FindTriggerAnimationsAsync(trigger);
            var forced = animations.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Animation))
                         ?? throw new InvalidDataException(animations.Count == 0
                             ? "The trigger has no Force Animation."
                             : "The Force Animation's animation name could not be resolved from the trigger's variables.");

            var root = await LoadMovesRootAsync();
            var move = FindMove(root!, forced.Animation) ?? throw new InvalidDataException($"Could not find <Moves><{forced.Animation} ... /> in moves.xml.");
            string? fileName = (string?)move.Attribute("FileName");
            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidDataException($"Move '{forced.Animation}' does not have a FileName attribute.");
            string pivot = (string?)move.Attribute("PivotNode") is { } p && !string.IsNullOrWhiteSpace(p) ? p : "NPivot";
            // Frame -1 (or none): the move's own FirstFrame, as in the game.
            int startFrame = forced.Frame;
            if (startFrame < 0)
                startFrame = int.TryParse((string?)move.Attribute("FirstFrame"), out int ff) ? ff : 0;
            string? path = await ResolveProjectFileAsync($"{BinsRelativeFolder}/{fileName}")
                           ?? throw new FileNotFoundException("Animation bin not found: " + VPath.Combine(_editor.Project.Path, BinsRelativeFolder, fileName));
            byte[] bin = await AppServices.FileSystem.ReadAllBytesAsync(path);

            var model = await EnsureModelAsync();
            if (model == null) return;
            if (!model.TryGetNodeIndex("COM", out int comIndex))
                throw new InvalidDataException("Node 'COM' was not found in the model XML.");
            if (!model.TryGetNodeIndex("DetectorH", out int detectorIndex))
                throw new InvalidDataException("Node 'DetectorH' was not found in the model XML.");

            StopPlacement();
            var host = (SceneElement)trigger;
            var bounds = trigger.GetLocalBounds().Translate(new Avalonia.Vector(trigger.ParentWorldPosition.X, trigger.ParentWorldPosition.Y));
            var target = ToSpace(new Point(forced.OnExit ? bounds.Right : bounds.Left, bounds.Bottom), host);

            // Place once, measure the start frame's COM and DetectorH, then move it so they land exactly.
            if (SpawnMode != MoveSpawnMode.Animation)
                SpawnMode = MoveSpawnMode.Animation;
            if (!Place(target, host, model, bin, pivot, startFrame, forced.Reversed) || Animation.AnimationNodes is not { } nodes)
                return;
            var shift = new Vector3(target.X - nodes[comIndex].X, target.Y - nodes[detectorIndex].Y, 0);
            if (Place(target + shift, host, model, bin, pivot, startFrame, forced.Reversed))
            {
                PivotNodeName = pivot;
                StatusMessage = Loc.F("{0} (frame {1}{2}), {3}", forced.Animation, startFrame,
                    forced.Reversed ? ", " + Loc.T("Reversed") : string.Empty,
                    forced.OnExit ? Loc.T("Model Leaves the Trigger") : Loc.T("Model Enters the Trigger"));
            }
        }
        catch (Exception ex)
        {
            Report(Loc.F("Preview Animation failed for '{0}': {1}", trigger.Name, ex.Message));
        }
    }

    // ---------------- Trick Area ----------------

    /// <summary>Trick Area "Preview Animation": the TrickGroup reaction named like the area (or its parent).</summary>
    public Task PreviewAreaAsync(AreaElement area) => PreviewAreaAsync(TargetFor(area));

    public async Task PreviewAreaAsync(AreaTarget target)
    {
        var area = target.Area;
        if (area.AreaType != "Trick")
        {
            Report(Loc.T("Preview Animation is only available when the Area's Type is Trick."));
            return;
        }
        try
        {
            var root = await LoadMovesRootAsync();
            var group = TrickGroup(root!) ?? throw new InvalidDataException("Could not find <ReactionGroup Name=\"TrickGroup\"> in moves.xml.");
            var candidates = new[] { area.Name, target.ParentName }
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToArray();
            bool Matches(XElement e, string attr) => (string?)e.Attribute(attr) is { } v && !string.IsNullOrWhiteSpace(v) &&
                                                      candidates.Any(c => string.Equals(v, c, StringComparison.OrdinalIgnoreCase));
            var reaction = group.Elements().FirstOrDefault(e => Matches(e, "TriggerName") || Matches(e, "AreaName"))
                           ?? throw new InvalidDataException($"Could not find a TrickGroup entry with TriggerName or AreaName matching: {string.Join(", ", candidates)}");
            string moveName = reaction.Name.LocalName;
            var move = FindMove(root!, moveName) ?? throw new InvalidDataException($"Could not find <Moves><{moveName} ... /> in moves.xml.");
            string? fileName = (string?)move.Attribute("FileName");
            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidDataException($"Move '{moveName}' does not have a FileName attribute.");
            string pivot = (string?)move.Attribute("PivotNode") is { } p && !string.IsNullOrWhiteSpace(p) ? p : "NPivot";
            int firstFrame = 0;
            if ((string?)reaction.Attribute("FirstFrame") is { } ff && !string.IsNullOrWhiteSpace(ff))
                int.TryParse(ff, out firstFrame);
            string? path = await ResolveProjectFileAsync($"{BinsRelativeFolder}/{fileName}")
                           ?? throw new FileNotFoundException("Animation bin not found: " + VPath.Combine(_editor.Project.Path, BinsRelativeFolder, fileName));
            byte[] bin = await AppServices.FileSystem.ReadAllBytesAsync(path);

            var model = await EnsureModelAsync();
            if (model == null) return;
            Animation.Reverse = Reverse;
            Animation.EnsureModelLoadedForPreview(model);

            // Unity GetAreaPreviewPosePlacementPosition: the COM sits 20 units past the area's far side,
            // the pivot on the area's bottom.
            if (!model.TryGetNodeIndex(pivot, out int pivotIndex))
                throw new InvalidDataException($"PivotNode '{pivot}' was not found in the model XML.");
            if (!model.TryGetNodeIndex("COM", out int comIndex))
                throw new InvalidDataException("Node 'COM' was not found in the model XML.");
            var pose = Animation.PreviewPose!;
            var o = target.Origin;
            var bounds = area.GetLocalBounds().Translate(new Avalonia.Vector(o.X, o.Y));
            const double finalXOffset = 20;
            double comToPivotX = pose[comIndex].X - pose[pivotIndex].X;
            double targetX = Reverse ? bounds.Left - finalXOffset - comToPivotX : bounds.Right + finalXOffset - comToPivotX;
            double targetY = bounds.Bottom; // Unity bounds.min.y (Y up) = the bottom edge

            StopPlacement();
            if (SpawnMode != MoveSpawnMode.Animation)
                SpawnMode = MoveSpawnMode.Animation;
            var host = target.Host;
            if (Place(ToSpace(new Point(targetX, targetY), host), host, model, bin, pivot, firstFrame))
                PivotNodeName = pivot;
        }
        catch (Exception ex)
        {
            Report(Loc.F("Preview Animation failed for '{0}': {1}", area.Name, ex.Message));
        }
    }
}
