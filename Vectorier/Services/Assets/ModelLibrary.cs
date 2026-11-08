using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;

namespace Vectorier.Services.Assets;

/// <summary>
/// A model file of the project as the Project Window and the Scene show it: the game's model (see
/// <see cref="ModelView"/>) at rest, its cloth settled, or why it cannot be drawn on its own.
/// </summary>
public sealed class LoadedModel
{
    public LoadedModel(string name, ModelView? view, string? problem)
    {
        Name = name;
        View = view;
        Problem = problem;
    }

    public string Name { get; }

    /// <summary>Null when the model cannot be drawn on its own (see <see cref="Problem"/>).</summary>
    public ModelView? View { get; }

    public string? Problem { get; }

    public bool IsSelfContained => View != null;

    /// <summary>The model relative to its NPivot (Y down). Call on the UI thread.</summary>
    public Geometry BuildSilhouette(out Rect bounds)
    {
        if (View == null)
            throw new InvalidOperationException(Problem);
        var geometry = View.Draw(View.Node("NPivot") ?? Vector3.Zero, out bounds);
        if (bounds.Width <= 0 && bounds.Height <= 0)
            bounds = new Rect(-40, -40, 80, 80);
        return geometry;
    }
}

/// <summary>Loads model XML files from a project's models folder and caches them.</summary>
public sealed class ModelLibrary
{
    /// <summary>Files listed in the Project Window but never rendered (speed; they need other files anyway).</summary>
    public static readonly HashSet<string> SkipRender = new(StringComparer.OrdinalIgnoreCase)
    {
        "0.xml", "1.xml", "2.xml", "cap.xml", "cap_2.xml", "GEAR_CAP.xml", "GEAR_SCARF.xml", "GEAR_SHORTS.xml",
        "helper.xml", "hunter.xml", "revolution_girl.xml", "shirt.xml", "tail.xml"
    };

    private readonly IFileSystem _fs;
    private readonly ProjectInfo _project;
    private readonly ConcurrentDictionary<string, Task<LoadedModel?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ModelLibrary(IFileSystem fs, ProjectInfo project)
    {
        _fs = fs;
        _project = project;
    }

    public async Task<IReadOnlyList<string>> GetModelFilesAsync()
    {
        var files = await _fs.GetFilesAsync(_project.ModelsFolder);
        return files.Where(f => VPath.HasExtension(f, ".xml")).ToList();
    }

    public static bool IsSkipped(string fileOrName)
    {
        string file = fileOrName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? VPath.GetFileName(fileOrName) : fileOrName + ".xml";
        return SkipRender.Contains(file);
    }

    /// <summary>Loads models/NAME.xml (cached). Returns null when it cannot be read.</summary>
    public Task<LoadedModel?> GetAsync(string name)
    {
        return _cache.GetOrAdd(name, async n =>
        {
            try
            {
                string path = VPath.Combine(_project.ModelsFolder, n + ".xml");
                if (!await _fs.FileExistsAsync(path))
                    return null;
                byte[] data = await _fs.ReadAllBytesAsync(path);
                return await Task.Run(() => Load(n, data));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModelLibrary] {n}: {ex.Message}");
                return null;
            }
        });
    }

    private static LoadedModel Load(string name, byte[] data)
    {
        try
        {
            var view = ModelView.Load(new[] { (name + ".xml", data) });
            view.Settle();
            return new LoadedModel(name, view, null);
        }
        catch (ModelLoadException ex)
        {
            return new LoadedModel(name, null, ex.Message);
        }
    }

    public void Invalidate(string? name = null)
    {
        if (name == null) _cache.Clear();
        else _cache.TryRemove(name, out _);
    }
}
