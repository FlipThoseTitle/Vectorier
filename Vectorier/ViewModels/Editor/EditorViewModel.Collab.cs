using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Collab;
using Vectorier.Services.Localization;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

/// <summary>Live Collaboration: what the session needs from the editor, and the dropdown's commands.</summary>
public sealed partial class EditorViewModel
{
    public CollabSession Collab => CollabSession.Current;

    public bool CanCollaborate => !AppServices.Platform.IsBrowser && ObjectEdit == null;

    /// <summary>The Live Collaboration button and menu item exist (desktop only).</summary>
    public bool CanCollaborateHere => !AppServices.Platform.IsBrowser;

    internal SceneXmlWriter CollabWriter() => CreateWriter();

    /// <summary>Where the pointer is over the Scene (level coordinates), null when it is elsewhere. Shown to the others.</summary>
    public Avalonia.Point? ScenePointer { get; set; }

    internal SceneXmlReader CollabReader => _reader;

    /// <summary>The host's level replaced this one: its history starts here, and it is not saved yet.</summary>
    internal void ResetHistoryAfterCollabLoad()
    {
        ResetHistory();
        IsDirty = true;
    }

    /// <summary>Someone else changed the level: it needs saving, and the views redraw.</summary>
    internal void SceneChangedByCollab()
    {
        IsDirty = true;
        SceneInvalidated?.Invoke();
    }

    internal async void ApplyCollabSettings(LevelSettings settings)
    {
        bool setsChanged = !settings.CitySets.SequenceEqual(Document.Settings.CitySets) ||
                           !settings.GroundSets.SequenceEqual(Document.Settings.GroundSets);
        Document.Settings = settings;
        IsDirty = true;
        if (setsChanged)
        {
            Context.Objects.Invalidate();
            await ResolveDefinitionsAsync(settings);
            using (SuppressTracking())
                ResolveReferences();
            RaiseStructureChanged();
        }
    }

    /// <summary>Elements selected here that someone else's Dynamic Editor now holds are let go.</summary>
    internal void DropBlockedSelection()
    {
        if (IsSelectionBlocked is not { } blocked || !Selection.Any(blocked))
            return;
        var keep = Selection.Where(e => !blocked(e)).ToList();
        using (SuppressTracking())
        {
            if (keep.Count == 0)
                ClearSelection();
            else
                Select(keep);
        }
    }

    /// <summary>The roots of the hierarchies this editor's Dynamic Editor is working on (sent to the others).</summary>
    internal List<long> DynamicEditorRoots() =>
        Dynamics.IsBound ? Dynamics.Tracks.Select(t => t.Element.Root.SyncId).Where(id => id != 0).Distinct().ToList() : new List<long>();

    /// <summary>Project files changed under the editor (downloaded from the host): textures, objects and lists are read again.</summary>
    internal async Task RefreshAfterFileSyncAsync(IReadOnlyCollection<string> changedPaths)
    {
        bool textures = changedPaths.Any(p => p.StartsWith("textures/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("animatedtextures/", StringComparison.OrdinalIgnoreCase));
        var xmlFiles = changedPaths.Where(p => p.StartsWith("xmlroot/", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("xmlroot/levels/", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[(p.LastIndexOf('/') + 1)..]).ToList();
        try
        {
            if (textures)
            {
                foreach (var path in changedPaths)
                    Context.Textures.Invalidate(System.IO.Path.GetFileNameWithoutExtension(path));
                await Context.Textures.RefreshIndexAsync();
            }
            if (xmlFiles.Count > 0)
                await RefreshDefinitionsAsync(xmlFiles);
            await ProjectWindow.ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        SceneInvalidated?.Invoke();
    }

    // ================= DROPDOWN COMMANDS ================= //

    /// <summary>Raised to open the Live Collaboration dropdown (File › Live Collaboration).</summary>
    public event Action? CollabDropdownRequested;

    [RelayCommand]
    private void OpenCollabDropdown() => CollabDropdownRequested?.Invoke();

    [RelayCommand]
    private Task StartCollab() => CollabUi.StartAsync(this);

    [RelayCommand]
    private Task StopCollab() => CollabUi.ConfirmAndEndAsync(this);

    [RelayCommand]
    private Task JoinCollab() => CollabUi.JoinAsync(this);

    [RelayCommand]
    private Task LeaveCollab() => CollabUi.ConfirmAndEndAsync(this);

    [RelayCommand]
    private Task ManageCollabUsers() => CollabUi.ManageUsersAsync();

    [RelayCommand]
    private async Task CopyCollabCode()
    {
        if (Collab.Code is { } code)
            await CollabUi.CopyAsync(code);
    }

    /// <summary>
    /// Before leaving the shared level (Project Manager, New Level, Open, closing): warns that it ends the session and
    /// ends it when confirmed. False: stay.
    /// </summary>
    public Task<bool> ConfirmLeaveCollabAsync() => CollabUi.ConfirmLeaveLevelAsync(this);
}
