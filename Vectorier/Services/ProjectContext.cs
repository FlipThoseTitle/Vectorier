using System;
using Vectorier.Services.Assets;
using Vectorier.Services.Projects;
using Vectorier.Services.Xml;

namespace Vectorier.Services;

/// <summary>
/// Per-project services shared by the Project Manager and the Level Editor. Only the project in use has one: opening
/// another project lets go of everything the previous one loaded.
/// </summary>
public sealed class ProjectContext
{
    private static ProjectContext? _current;

    private ProjectContext(ProjectInfo project)
    {
        Project = project;
        var fs = AppServices.FileSystem;
        Textures = new TextureLibrary(fs, project);
        Objects = new ObjectLibrary(fs, project.XmlRoot);
        Models = new ModelLibrary(fs, project);
        EditorCache = new EditorCache(fs, project);
    }

    public static ProjectContext For(ProjectInfo project)
    {
        if (_current != null && string.Equals(_current.Project.Path, project.Path, StringComparison.Ordinal))
            return _current;
        _current?.Release();
        _current = new ProjectContext(project);
        return _current;
    }

    /// <summary>
    /// A level editor closed: what only a level needs is let go (full-size textures, parsed object sets, models).
    /// The downscaled textures the Project Manager browses stay; the next level loads its own.
    /// </summary>
    public static void ReleaseEditorData(ProjectInfo project)
    {
        if (_current == null || !string.Equals(_current.Project.Path, project.Path, StringComparison.Ordinal))
            return;
        _current.Textures.ReleaseLoaded();
        _current.Objects.Invalidate();
        _current.Models.Invalidate();
    }

    private void Release()
    {
        Textures.ReleaseAll();
        Objects.Invalidate();
        Models.Invalidate();
    }

    public ProjectInfo Project { get; }
    public TextureLibrary Textures { get; }
    public ObjectLibrary Objects { get; }
    public ModelLibrary Models { get; }

    /// <summary>Project-specific organisational data of the editor (Project Window folders, stored triggers...).</summary>
    public EditorCache EditorCache { get; }
}
