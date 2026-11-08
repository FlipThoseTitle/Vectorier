using Vectorier.Services.IO;

namespace Vectorier.Services.Projects;

/// <summary>A project folder inside Projects.</summary>
public sealed class ProjectInfo
{
    public ProjectInfo(string name, string path, string displayPath)
    {
        Name = name;
        Path = path;
        DisplayPath = displayPath;
    }

    public string Name { get; }

    /// <summary>Relative path ("Projects/Name").</summary>
    public string Path { get; }

    /// <summary>Directory shown in the project list.</summary>
    public string DisplayPath { get; }

    public string XmlRoot => VPath.Combine(Path, "xmlroot");
    public string LevelsFolder => VPath.Combine(Path, "xmlroot", "levels");
    public string TexturesFolder => VPath.Combine(Path, "textures");
    public string AnimatedTexturesFolder => VPath.Combine(Path, "animatedtextures");
    public string ModelsFolder => VPath.Combine(Path, "models");
    public string AnimationsFolder => VPath.Combine(Path, "animations");
    public string CacheFolder => VPath.Combine(Path, ".vectorier");

    public override string ToString() => Name;
}
