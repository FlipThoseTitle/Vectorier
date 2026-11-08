using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

public partial class DynamicEditorViewModel
{
    private static string N(string? name) => string.IsNullOrEmpty(name) ? "Transform" : name;

    public void SelectTransformation(DynamicTrack track, string name)
    {
        ActiveTrack = track;
        track.TransformationName = name;
        LoadTransformation(track, name);
        track.IsModified = false;
        KeysChanged(track, edit: false);
        // Viewing another transformation is not a change: undo steps from now on start from it.
        _editor.RefreshHistoryBaseline();
    }

    /// <summary>Unity LoadFromDynamicTransform: rebuilds keys from a saved transformation, starting from the rest pose.</summary>
    private void LoadTransformation(DynamicTrack track, string name)
    {
        track.Keys.Clear();
        track.SelectedFrames.Clear();
        var rest = track.Original.Clone();
        rest.Frame = 0;
        track.Keys.Add(rest);

        var source = track.Element.Dynamics.FirstOrDefault(d => N(d.Name) == name)
                     ?? track.Element.InheritedDynamics.FirstOrDefault(d => N(d.Name) == name);
        if (source == null)
            return;

        int mc = source.Moves.Count, sc = source.Sizes.Count, rc = source.Rotations.Count, cc = source.Colors.Count;
        int n = Math.Max(Math.Max(mc, sc), Math.Max(rc, cc));
        var cur = rest.Clone();
        int frame = 0;
        for (int i = 0; i < n; i++)
        {
            var m = i < mc ? source.Moves[i] : null;
            var s = i < sc ? source.Sizes[i] : null;
            var r = i < rc ? source.Rotations[i] : null;
            var c = i < cc ? source.Colors[i] : null;

            if (m != null && (int)Math.Round(m.Delay) is int delay && delay > 0)
            {
                // A delay becomes a key with no movement.
                frame += delay;
                var hold = cur.Clone();
                hold.Frame = frame;
                hold.SupportX = hold.SupportY = 0.5;
                hold.Ease = EasePreset.Linear;
                track.Keys.Add(hold);
            }

            int df = 0;
            if (m != null) df = Math.Max(df, m.Frames);
            if (s != null) df = Math.Max(df, s.Frames);
            if (r != null) df = Math.Max(df, r.Frames);
            if (c != null) df = Math.Max(df, c.Frames);

            double mx = m?.FinishX ?? 0, my = m?.FinishY ?? 0;
            double sx = m?.SupportX ?? 0, sy = m?.SupportY ?? 0;
            var next = cur.Clone();
            if (m != null) { next.X += mx; next.Y += my; }
            if (r != null) next.Rotation = cur.Rotation + r.Angle;
            if (c != null) next.Color = c.Finish;
            if (s != null) { next.Width = s.FinalWidth; next.Height = s.FinalHeight; }

            if (CustomEase)
            {
                next.SupportX = sx;
                next.SupportY = sy;
                next.Ease = EasePreset.Custom;
            }
            else if (MatchPreset(mx, my, sx, sy, 0)) SetPreset(next, EasePreset.EaseIn);
            else if (MatchPreset(mx, my, sx, sy, 0.5)) SetPreset(next, EasePreset.Linear);
            else if (MatchPreset(mx, my, sx, sy, 1)) SetPreset(next, EasePreset.EaseOut);
            else
            {
                next.SupportX = sx;
                next.SupportY = sy;
                next.Ease = EasePreset.Custom;
            }

            frame += df;
            next.Frame = frame;
            track.Keys.Add(next);
            cur = next;
        }
        track.TotalFrames = Math.Max(Math.Max(1, frame + 1), track.TotalFrames);
        track.Sort();
    }

    private static void SetPreset(DynamicKey key, EasePreset ease)
    {
        key.Ease = ease;
        key.SupportX = key.SupportY = DynamicKey.PresetValue(ease);
    }

    private static bool MatchPreset(double mx, double my, double sx, double sy, double k)
    {
        bool okx = Math.Abs(mx) <= Eps ? Math.Abs(sx) <= Eps : Math.Abs(sx - mx * k) <= Eps;
        bool oky = Math.Abs(my) <= Eps ? Math.Abs(sy) <= Eps : Math.Abs(sy - my * k) <= Eps;
        return okx && oky;
    }

    /// <summary>Unity BakeToDynamicTransform: turns the keys into Move / Size / Rotation / Color intervals.</summary>
    public DynamicTransformation Bake(DynamicTrack track, string name)
    {
        var t = new DynamicTransformation { Name = name };
        track.Sort();
        var keys = track.Keys;
        if (keys.Count < 2)
            return t;

        var e = track.Element;
        var k0 = keys[0];
        bool anyMove = false, anySize = false, anyRot = false, anyColor = false;
        foreach (var k in keys.Skip(1))
        {
            anyMove |= Math.Abs(k.X - k0.X) > 1e-6 || Math.Abs(k.Y - k0.Y) > 1e-6;
            anySize |= e.HasSize && (Math.Abs(k.Width - k0.Width) > Eps || Math.Abs(k.Height - k0.Height) > Eps);
            anyRot |= Math.Abs(k.Rotation - k0.Rotation) > Eps;
            anyColor |= k.Color != k0.Color;
        }

        double pendingDelay = 0;
        for (int i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            int df = Math.Max(0, b.Frame - a.Frame);
            if (df <= 0) continue;

            if (anyMove)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y;
                if (Math.Abs(dx) <= 1e-6 && Math.Abs(dy) <= 1e-6)
                {
                    pendingDelay += df;
                }
                else
                {
                    double supX, supY;
                    if (CustomEase || b.Ease == EasePreset.Custom)
                    {
                        supX = b.SupportX;
                        supY = b.SupportY;
                    }
                    else
                    {
                        double s = DynamicKey.PresetValue(b.Ease);
                        supX = dx * s;
                        supY = dy * s;
                    }
                    t.Moves.Add(new DynamicMove { Frames = df, Delay = pendingDelay, FinishX = Round(dx), FinishY = Round(dy), SupportX = Round(supX), SupportY = Round(supY) });
                    pendingDelay = 0;
                }
            }

            if (anySize)
            {
                var last = t.Sizes.LastOrDefault();
                if (last != null && Math.Abs(last.FinalWidth - b.Width) <= Eps && Math.Abs(last.FinalHeight - b.Height) <= Eps)
                    last.Frames += df;
                else
                    t.Sizes.Add(new DynamicSize { Frames = df, FinalWidth = Round(b.Width), FinalHeight = Round(b.Height) });
            }

            if (anyRot)
            {
                double delta = ((b.Rotation - a.Rotation) % 360 + 540) % 360 - 180;
                var last = t.Rotations.LastOrDefault();
                if (last != null && Math.Abs(last.Angle - delta) <= Eps && last.AnchorX == 0 && last.AnchorY == 0)
                    last.Frames += df;
                else
                    t.Rotations.Add(new DynamicRotation { Angle = Round(delta), Frames = df });
            }

            if (anyColor)
            {
                var last = t.Colors.LastOrDefault();
                if (last != null && last.Start == a.Color && last.Finish == b.Color)
                    last.Frames += df;
                else
                    t.Colors.Add(new DynamicColor { Start = a.Color, Finish = b.Color, Frames = df });
            }
        }
        return t;
    }

    private static double Round(double v) => Math.Round(v, 4);

    /// <summary>Save button: asks for the transformation's name, then bakes the timeline into it.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task SaveTransformation(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        await SaveWithPromptAsync(track);
    }

    /// <summary>
    /// Asks for the name (the picked transformation's, or a new one) and saves. Saving under another name keeps the
    /// picked transformation and adds a new one. False when cancelled.
    /// </summary>
    public async System.Threading.Tasks.Task<bool> SaveWithPromptAsync(DynamicTrack track)
    {
        ActiveTrack = track;
        string current = N(track.TransformationName);
        bool exists = track.Element.Dynamics.Any(d => N(d.Name) == current) || IsInherited(track, current);
        string initial = exists ? current : UniqueName(track, "Transform", null);
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Save Transformation"),
            Loc.F("Transformation name for '{0}':", track.Header), initial,
            text => ValidateSaveName(track, text, exists ? current : null), Loc.T("Save"));
        if (name == null)
            return false;
        SaveAs(track, name.Trim());
        return true;
    }

    private static string? ValidateSaveName(DynamicTrack track, string text, string? current)
    {
        string name = text.Trim();
        if (name.Length == 0)
            return Loc.T("Enter a name.");
        if (name != current && (track.Element.Dynamics.Any(d => N(d.Name) == name) || IsInherited(track, name)))
            return Loc.F("'{0}' already has a transformation named '{1}'.", track.Header, name);
        return null;
    }

    /// <summary>Unity "Save": bakes the timeline into the element's transformation with this name.</summary>
    private void SaveAs(DynamicTrack track, string name)
    {
        name = N(name);
        var baked = Bake(track, name);
        // A transformation of the object's definition (not the instance's own): saved into the definition, in its
        // Object XML, so every instance runs it (as the game does), instead of a second copy on this instance.
        if (IsInherited(track, name) && track.Element is ObjectElement { Reference: { } definition })
        {
            int at = definition.Dynamics.FindIndex(d => N(d.Name) == name);
            definition.Dynamics[at] = baked;
            track.TransformationName = name;
            RefreshNames(track);
            track.IsModified = false;
            _ = _editor.SaveDefinitionDynamicsAsync(definition);
            RaiseTimeline();
            return;
        }
        var list = track.Element.Dynamics;
        int index = list.FindIndex(d => N(d.Name) == name);
        if (index >= 0) list[index] = baked;
        else list.Add(baked);
        track.TransformationName = name;
        RefreshNames(track);
        track.IsModified = false;
        Share(track);
        _editor.MarkChanged();
        _editor.StatusText = Loc.F("Saved transformation '{0}' on {1}", name, track.Header);
        RaiseTimeline();
    }

    /// <summary>
    /// Timelines with changes not baked into a transformation: asks whether to save them (one name prompt per
    /// element, in turn). False when the user cancels.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> ResolveUnsavedAsync(string title)
    {
        var pending = Tracks.Where(t => t.IsModified).ToList();
        if (pending.Count == 0)
            return true;
        string message = pending.Count == 1
            ? Loc.F("The timeline of '{0}' has changes that are not saved into a transformation yet. Save them?", pending[0].Header)
            : Loc.F("{0} timelines have changes that are not saved into a transformation yet. Save them? You will name each transformation in turn.", pending.Count);
        var dialog = new ViewModels.Dialogs.MessageDialogViewModel(title, message, MessageKind.Warning);
        dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Save"), "Save", isDefault: true));
        dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Don't Save"), "Discard"));
        dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Cancel"), null, isCancel: true));
        switch (await AppServices.Dialogs.ShowAsync(dialog))
        {
            case "Save":
                foreach (var track in pending)
                {
                    if (!Tracks.Contains(track))
                        continue;
                    if (!await SaveWithPromptAsync(track))
                        return false;
                }
                return true;
            case "Discard":
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private void NewTransformation(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        string name = UniqueName(track, "NewTransform", null);
        track.Element.Dynamics.Add(new DynamicTransformation { Name = name });
        RefreshNames(track);
        track.TransformationName = name;
        LoadTransformation(track, name);
        track.IsModified = false;
        KeysChanged(track, edit: false);
        Share(track);
        _editor.MarkChanged();
    }

    [RelayCommand]
    private void RemoveTransformation(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        string name = N(track.TransformationName);
        if (track.Element.Dynamics.RemoveAll(d => N(d.Name) == name) > 0)
            _editor.MarkChanged();
        RefreshNames(track);
        track.TransformationName = track.TransformationNames.FirstOrDefault() ?? "Transform";
        RefreshNames(track);
        LoadTransformation(track, track.TransformationName);
        track.IsModified = false;
        KeysChanged(track, edit: false);
        Share(track);
        _editor.MarkChanged();
    }

    public void RenameTransformation(DynamicTrack track, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        string current = N(track.TransformationName);
        string unique = UniqueName(track, newName.Trim(), current);
        var existing = track.Element.Dynamics.FirstOrDefault(d => N(d.Name) == current);
        if (existing != null)
        {
            existing.Name = unique;
            Share(track);
            _editor.MarkChanged();
        }
        track.TransformationName = unique;
        RefreshNames(track);
        RaiseTimeline();
    }

    /// <summary>The name is one of the definition's transformations, not the element's own.</summary>
    private static bool IsInherited(DynamicTrack track, string name) =>
        !track.Element.Dynamics.Any(d => N(d.Name) == name) && track.Element.InheritedDynamics.Any(d => N(d.Name) == name);

    private static string UniqueName(DynamicTrack track, string requested, string? exclude)
    {
        var names = track.Element.Dynamics.Select(d => N(d.Name))
            .Concat(track.Element.InheritedDynamics.Select(d => N(d.Name))).Where(n => n != exclude).ToHashSet();
        if (!names.Contains(requested)) return requested;
        string candidate = requested + "_copy";
        if (!names.Contains(candidate)) return candidate;
        int i = 1;
        while (names.Contains($"{requested}_copy_{i}")) i++;
        return $"{requested}_copy_{i}";
    }
}
