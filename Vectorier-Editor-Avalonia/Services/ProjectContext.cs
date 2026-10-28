using System;
using System.Collections.Generic;
using Vectorier_Editor_Avalonia.Services.Assets;
using Vectorier_Editor_Avalonia.Services.Projects;
using Vectorier_Editor_Avalonia.Services.Xml;

namespace Vectorier_Editor_Avalonia.Services;

/// <summary>Per-project services shared by the Project Manager and the Level Editor.</summary>
public sealed class ProjectContext
{
    private static readonly Dictionary<string, ProjectContext> Cache = new(StringComparer.Ordinal);

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
        if (!Cache.TryGetValue(project.Path, out var context))
        {
            context = new ProjectContext(project);
            Cache[project.Path] = context;
        }
        return context;
    }

    public ProjectInfo Project { get; }
    public TextureLibrary Textures { get; }
    public ObjectLibrary Objects { get; }
    public ModelLibrary Models { get; }

    /// <summary>Project-specific organisational data of the editor (Project Window folders, stored triggers...).</summary>
    public EditorCache EditorCache { get; }
}
