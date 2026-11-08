#nullable disable warnings
namespace Vectorier.Gameplay;

/// <summary>
/// Linux and macOS file systems can be case-sensitive while Vector's XML names files the Windows way
/// ("Buildings.xml" for buildings.xml). Resolve finds the file with any casing; on Windows it does nothing.
/// </summary>
/// <summary>The game's file names are found whatever their casing: the editor's store (GameFiles) ignores case.</summary>
public static class PathCase
{
    public static void Clear() { }

    public static string Resolve(string path) => path;
}
