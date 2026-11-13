#nullable enable
using System;
using System.Xml;

namespace Vectorier.Gameplay.Diagnostics;

/// <summary>
/// The game's data is wrong in a way the game cannot play with: a file that is missing or is not XML, a required
/// element or attribute that is missing, or a value that cannot be read. It names where (the file, the element
/// and the attribute) so the problem can be found without guessing; <see cref="GameErrorReport"/> turns it (and
/// any other exception) into what the editor shows and logs.
/// </summary>
public sealed class GameDataException : Exception
{
    public GameDataException(string problem, string? file = null, XmlNode? node = null, string? attribute = null,
        int line = 0, int column = 0, Exception? inner = null)
        : base(Describe(problem, file, node, attribute), inner)
    {
        Problem = problem;
        File = file ?? XmlSource.FileOf(node);
        Node = node;
        Attribute = attribute;
        Line = line;
        Column = column;
    }

    /// <summary>What is wrong, on its own (without where).</summary>
    public string Problem { get; }

    /// <summary>The game file (as the game names it, e.g. animations/moves.xml), when known.</summary>
    public string? File { get; }

    /// <summary>The element that is wrong, when known.</summary>
    public XmlNode? Node { get; }

    public string? Attribute { get; }

    /// <summary>1-based position in the file; 0 when not known (it is then looked up from <see cref="Node"/>).</summary>
    public int Line { get; }
    public int Column { get; }

    private static string Describe(string problem, string? file, XmlNode? node, string? attribute)
    {
        file ??= XmlSource.FileOf(node);
        string where = string.Empty;
        if (file != null)
            where += file;
        if (node != null)
            where += (where.Length > 0 ? ", " : string.Empty) + XmlSource.Describe(node);
        if (attribute != null)
            where += (where.Length > 0 ? ", " : string.Empty) + "attribute \"" + attribute + "\"";
        return where.Length > 0 ? problem + " (" + where + ")" : problem;
    }
}
