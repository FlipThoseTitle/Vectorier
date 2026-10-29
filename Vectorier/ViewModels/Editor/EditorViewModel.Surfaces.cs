using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Vectorier.Models.Scene;
using Vectorier.Services.Assets;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// Live platform surfaces (Unity's PlatformSurfaceWatcher): a Platform with Rough / Smooth / Block settings has its
/// textures rebuilt whenever it is moved, resized (directly or by scaling its Object) or its settings change.
/// Requests are collapsed and flushed on the next UI tick; a pending rebuild is always flushed before an undo step
/// is committed, so the rebuilt textures belong to the same step as the change that caused them.
/// </summary>
public sealed partial class EditorViewModel
{
    private readonly HashSet<PlatformElement> _surfacePending = new();
    private bool _surfaceFlushPosted;
    private DispatcherTimer? _surfaceTimer;
    private bool _surfaceFlushing;

    private static readonly HashSet<string> SurfaceProperties = new(StringComparer.Ordinal)
    {
        nameof(SceneElement.X), nameof(SceneElement.Y), nameof(SceneElement.Width), nameof(SceneElement.Height),
        nameof(PlatformElement.Surface),
    };

    private void OnSurfacePropertyChanged(SceneElement element, string? property)
    {
        if (element is PlatformElement { Surface: not null } platform && property != null && SurfaceProperties.Contains(property))
            RequestSurfaceRebuild(platform);
    }

    /// <summary>Queues a rebuild for the next tick (several requests for the same platform are one rebuild).</summary>
    public void RequestSurfaceRebuild(PlatformElement platform)
    {
        if (_restoring || _surfaceFlushing || platform.Surface == null)
            return;
        _surfacePending.Add(platform);
        if (_surfaceFlushPosted)
            return;
        _surfaceFlushPosted = true;
        // Not inside the change itself: a scale may still be iterating the Object's children.
        // At most every 0.1 s while dragging (rebuilding every frame lags); an undo step flushes at once.
        _surfaceTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            _surfaceTimer!.Stop();
            if (_surfaceFlushPosted)
                FlushSurfaces();
        });
        _surfaceTimer.Start();
    }

    /// <summary>Rebuilds the queued surfaces that are out of date.</summary>
    public void FlushSurfaces()
    {
        _surfaceFlushPosted = false;
        if (_surfacePending.Count == 0 || _surfaceFlushing)
            return;
        if (!PlatformSurface.IsCatalogReady(Context.Textures))
        {
            // First use: wait for the texture sizes, then try again.
            _ = PlatformSurface.EnsureCatalogAsync(Context.Textures).ContinueWith(
                _ => Dispatcher.UIThread.Post(() => FlushSurfaces()), TaskScheduler.Default);
            return;
        }

        var batch = _surfacePending.ToList();
        _surfacePending.Clear();
        bool changed = false;
        _surfaceFlushing = true;
        try
        {
            foreach (var platform in batch)
                changed |= BuildSurface(platform, force: false);
        }
        finally
        {
            _surfaceFlushing = false;
        }
        if (changed)
        {
            MarkChanged();
            RaiseStructureChanged();
        }
    }

    private bool IsInDocument(SceneElement element) => element.Root is var root && Document.Roots.Contains(root);

    private ObservableCollection<SceneElement> SiblingsOf(SceneElement element) => element.Parent?.Children ?? Document.Roots;

    /// <summary>The images generated for a platform.</summary>
    /// <summary>
    /// The Object made for an auto-built platform: its direct parent when that Object holds only the platform and the
    /// platform's own textures. Null when the platform shares its parent with anything else (then only the platform
    /// and its textures belong to it).
    /// </summary>
    public ObjectElement? SurfaceHost(PlatformElement platform)
    {
        if (platform.Surface == null || platform.Parent is not { IsReference: false } parent)
            return null;
        foreach (var child in parent.Children)
        {
            if (ReferenceEquals(child, platform))
                continue;
            if (child is ImageElement { SurfaceOwner: var owner } && owner == platform.Id)
                continue;
            return null;
        }
        return parent;
    }

    /// <summary>
    /// Delete / Duplicate targets: an auto-built platform stands for its whole Object (or, without one, for itself and
    /// its textures). Nothing else is added, and nothing is listed twice.
    /// </summary>
    private List<SceneElement> ExpandSurfaceTargets(IReadOnlyList<SceneElement> targets, bool includeLooseTiles)
    {
        var result = new List<SceneElement>();
        foreach (var element in targets)
        {
            if (element is PlatformElement { Surface: not null } platform)
            {
                if (SurfaceHost(platform) is { } host)
                {
                    result.Add(host);
                    continue;
                }
                result.Add(platform);
                if (includeLooseTiles)
                    result.AddRange(SurfaceTiles(platform));
                continue;
            }
            result.Add(element);
        }
        return result.Distinct().Where(e => !result.Any(o => !ReferenceEquals(o, e) && e.IsDescendantOf(o))).ToList();
    }

    public List<ImageElement> SurfaceTiles(PlatformElement platform) =>
        SiblingsOf(platform).OfType<ImageElement>().Where(i => i.SurfaceOwner == platform.Id).ToList();

    /// <summary>
    /// Regenerates a platform's textures (hash check makes moves of the whole Object and no-op edits free).
    /// </summary>
    private bool BuildSurface(PlatformElement platform, bool force)
    {
        var settings = platform.Surface;
        if (settings == null || !IsInDocument(platform))
            return false;
        string key = PlatformSurface.BuildKey(platform, settings);
        var siblings = SiblingsOf(platform);
        var old = siblings.OfType<ImageElement>().Where(i => i.SurfaceOwner == platform.Id).ToList();
        if (!force && key == platform.SurfaceBuiltKey && old.Count > 0)
            return false;

        // Keep the selection on the platform rather than on tiles that are about to disappear.
        bool selectionTouched = false;
        foreach (var tile in old)
        {
            if (tile.IsSelected)
            {
                tile.IsSelected = false;
                _selection.Remove(tile);
                selectionTouched = true;
            }
            siblings.Remove(tile);
            tile.Parent = null;
        }

        var tiles = PlatformSurface.BuildTiles(settings, platform);
        int index = siblings.IndexOf(platform);
        foreach (var tile in tiles)
        {
            if (platform.Parent is { } parent)
                parent.AddChild(tile, index++);
            else
                siblings.Insert(index++, tile);
        }
        platform.SurfaceBuiltKey = key;
        if (selectionTouched)
        {
            if (ActiveElement is ImageElement { SurfaceOwner: not 0 })
                ActiveElement = _selection.LastOrDefault();
            RaiseSelectionChanged();
        }
        return true;
    }

    private void WrapInObject(PlatformElement platform)
    {
        int index = Document.Roots.IndexOf(platform);
        var group = new ObjectElement
        {
            X = platform.X,
            Y = platform.Y,
            Layer = platform.Layer,
            Order = platform.Order,
            Name = string.Empty,
        };
        Document.Roots.RemoveAt(index);
        platform.X = 0;
        platform.Y = 0;
        group.AddChild(platform);
        Document.Roots.Insert(index, group);
    }

    // ================= INSPECTOR ACTIONS ================= //

    /// <summary>Gives a plain platform a surface (Unity's Quick Surface / adding a PlatformComponent).</summary>
    public async Task AddSurfaceAsync(PlatformElement platform, PlatformSurfaceType type)
    {
        await PlatformSurface.EnsureCatalogAsync(Context.Textures);
        CommitPending();
        // In a level, the platform and its textures are kept together in an Object (like placing a surface).
        if (platform.Parent == null && ObjectEdit == null && Document.Roots.Contains(platform))
            WrapInObject(platform);
        platform.Surface = new PlatformSurfaceSettings(type, Seed: PlatformSurfaceSettings.NewSeed());
        RebuildSurfaceNow(platform, force: true);
        CommitPending();
        StatusText = $"Platform surface: {type}";
    }

    /// <summary>Removes the surface and its generated textures (the platform itself stays).</summary>
    public void RemoveSurface(PlatformElement platform)
    {
        if (platform.Surface == null)
            return;
        CommitPending();
        foreach (var tile in SurfaceTiles(platform))
        {
            if (tile.IsSelected)
            {
                tile.IsSelected = false;
                _selection.Remove(tile);
            }
            SiblingsOf(platform).Remove(tile);
            tile.Parent = null;
        }
        _surfacePending.Remove(platform);
        platform.Surface = null;
        platform.SurfaceBuiltKey = null;
        MarkChanged();
        RaiseStructureChanged();
        RaiseSelectionChanged();
        CommitPending();
        StatusText = "Platform surface removed";
    }

    /// <summary>"Rebuild Surface": regenerates even when nothing changed (picks up re-imported textures).</summary>
    public async Task RebuildSurfaceAsync(IEnumerable<PlatformElement> platforms)
    {
        await PlatformSurface.EnsureCatalogAsync(Context.Textures, reload: true);
        CommitPending();
        foreach (var platform in platforms.ToList())
            RebuildSurfaceNow(platform, force: true);
        CommitPending();
    }

    private void RebuildSurfaceNow(PlatformElement platform, bool force)
    {
        _surfacePending.Remove(platform);
        _surfaceFlushing = true;
        bool changed;
        try
        {
            changed = BuildSurface(platform, force);
        }
        finally
        {
            _surfaceFlushing = false;
        }
        if (changed)
        {
            MarkChanged();
            RaiseStructureChanged();
        }
    }

    // ================= EDITOR CACHE ================= //

    private string SurfaceCacheKey => ObjectEdit is { } session
        ? $"object:{session.File}:{session.Name}"
        : Document.FilePath ?? Document.LevelName;

    private static Rect WorldRect(SceneElement element)
    {
        var p = element.WorldPosition;
        return new Rect(p.X, p.Y, element.Width, element.Height);
    }

    /// <summary>
    /// Gives the platforms of a freshly opened document their cached surface settings and adopts the textures
    /// that were generated for them (images with surface texture names inside the platform, in the same Object).
    /// </summary>
    private void RestoreSurfaces(SceneDocument doc)
    {
        if (!Context.EditorCache.Data.PlatformSurfaces.TryGetValue(SurfaceCacheKey, out var stored) || stored.Count == 0)
            return;
        foreach (var platform in doc.AllElements().OfType<PlatformElement>())
        {
            if (platform.Surface != null)
                continue;
            var rect = WorldRect(platform);
            var match = stored.FirstOrDefault(s =>
                Math.Abs(s.X - rect.X) < 0.5 && Math.Abs(s.Y - rect.Y) < 0.5 && Math.Abs(s.Width - rect.Width) < 0.5 && Math.Abs(s.Height - rect.Height) < 0.5);
            if (match == null || !Enum.TryParse<PlatformSurfaceType>(match.Type, out var type))
                continue;
            var settings = new PlatformSurfaceSettings(type, match.Floor, match.LeftWall, match.RightWall, match.Seed);
            platform.Surface = settings;
            var local = new Rect(platform.X, platform.Y, platform.Width, platform.Height).Inflate(1);
            var siblings = platform.Parent?.Children ?? doc.Roots;
            foreach (var image in siblings.OfType<ImageElement>())
            {
                if (image.SurfaceOwner == 0 && PlatformSurface.IsSurfaceTexture(image.Name) && local.Contains(image.GetLocalBounds()))
                    image.SurfaceOwner = platform.Id;
            }
            platform.SurfaceBuiltKey = PlatformSurface.BuildKey(platform, settings);
        }
    }

    /// <summary>On save: the cache entries of the level (or object) become exactly its surfaced platforms.</summary>
    private async Task SaveSurfacesAsync()
    {
        var list = Document.AllElements().OfType<PlatformElement>()
            .Where(p => p.Surface != null)
            .Select(p =>
            {
                var r = WorldRect(p);
                var s = p.Surface!;
                return new StoredSurface
                {
                    X = r.X, Y = r.Y, Width = r.Width, Height = r.Height,
                    Type = s.Type.ToString(), Floor = s.Floor, LeftWall = s.LeftWall, RightWall = s.RightWall, Seed = s.Seed,
                };
            })
            .ToList();
        var all = Context.EditorCache.Data.PlatformSurfaces;
        if (list.Count > 0) all[SurfaceCacheKey] = list;
        else if (!all.Remove(SurfaceCacheKey)) return;
        await Context.EditorCache.SaveAsync();
    }
}
