using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

public partial class DynamicEditorViewModel
{
    /// <summary>Path of the element's pivot over the whole timeline (drawn in the Scene).</summary>
    public List<Point> SamplePath(DynamicTrack track, int step = 2)
    {
        var points = new List<Point>();
        if (track.Keys.Count < 2) return points;
        int end = Math.Max(track.Keys[^1].Frame, 1);
        for (int f = 0; f <= end; f += step)
        {
            var p = Evaluate(track, f);
            points.Add(new Point(p.X, p.Y));
        }
        var last = Evaluate(track, end);
        points.Add(new Point(last.X, last.Y));
        return points;
    }

    /// <summary>Previous and next keys around the playhead (onion skin).</summary>
    public IEnumerable<DynamicKey> OnionKeys(DynamicTrack track)
    {
        int f = TrackFrame(track);
        var prev = track.Keys.Where(k => k.Frame < f).OrderByDescending(k => k.Frame).FirstOrDefault();
        var next = track.Keys.Where(k => k.Frame > f).OrderBy(k => k.Frame).FirstOrDefault();
        if (prev != null) yield return prev;
        if (next != null) yield return next;
    }

    /// <summary>The segment the playhead is in, for the custom-ease support handle.</summary>
    public (DynamicKey A, DynamicKey B)? CurrentSegment(DynamicTrack track)
    {
        int f = TrackFrame(track);
        var keys = track.Keys;
        for (int i = 0; i < keys.Count - 1; i++)
        {
            if (f > keys[i].Frame && f <= keys[i + 1].Frame)
                return (keys[i], keys[i + 1]);
        }
        if (keys.Count >= 2 && f == keys[0].Frame)
            return (keys[0], keys[1]);
        return null;
    }

    /// <summary>Scene drag of the support handle: sets the segment end key's support (custom ease).</summary>
    public void SetSupport(DynamicTrack track, DynamicKey a, DynamicKey b, Point absolute)
    {
        b.SupportX = absolute.X - a.X;
        b.SupportY = absolute.Y - a.Y;
        b.Ease = EasePreset.Custom;
        KeysChanged(track);
    }

    /// <summary>
    /// Adds a trigger at the hierarchy level of the edited elements: when a model enters it, it plays the
    /// transformation of each edited element (one Play Transformation per distinct name).
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task AddTriggerAsync()
    {
        if (Tracks.Count == 0)
            return;
        // The trigger plays transformations by name: unsaved timelines are saved (and named) first.
        if (!await ResolveUnsavedAsync(Loc.T("Add Trigger")) || Tracks.Count == 0)
            return;
        var first = Tracks[0].Element;
        var parent = first.Parent;
        // Elements at the same level as the first one decide the content; the trigger goes right above them.
        var level = Tracks.Where(t => ReferenceEquals(t.Element.Parent, parent)).ToList();
        var siblings = _editor.Document.SiblingsOf(first);
        int index = level.Select(t => siblings.IndexOf(t.Element)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();

        // Only transformations that exist on their element (a timeline never saved has none to play).
        var names = level.Where(t => t.Element.Dynamics.Any(d => N(d.Name) == N(t.TransformationName)))
            .Select(t => t.TransformationName?.Trim() ?? string.Empty)
            .Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        // Several transformations: run them one after another, at the same time, or a random few.
        string order = "Straight";
        int randomCount = 1;
        if (level.Count > 1 && names.Count > 1)
        {
            var dialog = new ViewModels.Dialogs.MessageDialogViewModel(Loc.T("Add Trigger"),
                Loc.F("How should the {0} transformations run when the trigger fires?", names.Count), MessageKind.Question);
            dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Run After Another"), "Straight", isDefault: true));
            dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Run at the Same Time"), "Sync"));
            dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Run Randomly"), "Random"));
            dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Cancel"), null, isCancel: true));
            if (await AppServices.Dialogs.ShowAsync(dialog) is not string picked)
                return;
            order = picked;
            if (order == "Random")
            {
                int available = names.Count;
                string? text = await AppServices.Dialogs.PromptAsync(Loc.T("Run Randomly"),
                    Loc.F("How many of the {0} transformations should run (picked at random)?", available), "1",
                    value => ValidateRandomCount(value, available), Loc.T("Add Trigger"));
                if (text == null)
                    return;
                Controls.MathFields.TryEvaluate(text.Trim(), System.Globalization.CultureInfo.CurrentCulture.NumberFormat, out decimal picked2);
                randomCount = (int)picked2;
            }
        }

        string indent = order == "Straight" ? "      " : "        ";
        var transforms = string.Concat(names.Select(n => $"\n{indent}<Transform Name=\"{System.Security.SecurityElement.Escape(n)}\"/>"));
        var actions = order switch
        {
            // Run Order Group (Choose): Sync runs them all together, Random runs Set of them.
            "Sync" => "\n      <Choose Order=\"Sync\">" + transforms + "\n      </Choose>",
            "Random" => $"\n      <Choose Order=\"Random\" Set=\"{randomCount}\">" + transforms + "\n      </Choose>",
            _ => transforms,
        };
        string content =
            "\n  <Init>" +
            "\n    <SetVariable Name=\"$Active\" Value=\"1\"/>" +
            "\n    <SetVariable Name=\"$AI\" Value=\"0\"/>" +
            "\n    <SetVariable Name=\"$Node\" Value=\"COM\"/>" +
            "\n  </Init>" +
            "\n  <Loop>" +
            "\n    <Events>" +
            "\n      <EventBlock Template=\"FreqUsed.Enter\"/>" +
            "\n    </Events>" +
            "\n    <Actions>" +
            "\n      <ActionBlock Template=\"FreqUsed.SwitchOff\"/>" + actions +
            "\n    </Actions>" +
            "\n  </Loop>\n";

        var at = first.WorldPosition;
        var created = await _editor.CreateElementAsync(ElementType.Trigger, new Point(at.X, at.Y), null, null, parent, index);
        if (created is not TriggerElement trigger)
            return;
        trigger.Name = "TriggerTransform";
        trigger.ContentXml = content;
        // The new trigger becomes the only selection and the Scene frames it.
        _editor.Select(trigger);
        _editor.RequestFrame(trigger);
        _editor.StatusText = names.Count == 0
            ? Loc.T("Added a trigger (no transformation name to play).")
            : Loc.F("Added a trigger that plays {0}.", string.Join(", ", names));
    }

    /// <summary>Run Randomly: a whole number from 1 to the number of transformations.</summary>
    private static string? ValidateRandomCount(string value, int available)
    {
        if (!Controls.MathFields.TryEvaluate(value.Trim(), System.Globalization.CultureInfo.CurrentCulture.NumberFormat, out decimal number) || number != Math.Truncate(number)
            || number < int.MinValue || number > int.MaxValue)
            return Loc.T("Enter a whole number (for example 1).");
        int count = (int)number;
        if (count < 0)
            return Loc.T("Warning: the number can't be negative.");
        if (count == 0)
            return Loc.T("Warning: at least 1 transformation has to run.");
        if (count > available)
            return Loc.F("Error: there are only {0} transformations.", available);
        return null;
    }
}
