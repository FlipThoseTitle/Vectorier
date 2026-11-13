#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Vectorier.Gameplay.Diagnostics;

namespace Vectorier.Gameplay.Utilities;

public static class XmlUtils
{
    public enum OpenXmlType
    {
        Normal = 0,
        ForcedResourced = 1,
        ForcedExternal = 2
    }

    public const string Comment = "#comment";

    public static int ParseInt(this XmlAttribute attr, int defVal = 0)
    {
        if (attr == null || !int.TryParse(attr.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
        {
            return defVal;
        }
        return result;
    }

    public static float ParseFloat(this XmlAttribute attr, float defVal = 0f)
    {
        if (attr == null || !float.TryParse(attr.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
        {
            return defVal;
        }
        return result;
    }

    public static bool ParseBool(this XmlAttribute attr, bool defVal = false)
    {
        if (attr == null || !int.TryParse(attr.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
        {
            return defVal;
        }
        return result > 0;
    }

    public static string ParseString(this XmlAttribute attr, string defVal = null)
    {
        if (attr == null)
        {
            return defVal;
        }
        return attr.Value;
    }

    public static XmlDocument OpenXMLDocument(string path, string file = "", OpenXmlType openType = OpenXmlType.Normal, bool pIgnoreComments = true, bool dialog = true)
    {
        try
        {
            return ReadXmlDocument(path, file, pIgnoreComments);
        }
        catch (GameDataException ex)
        {
            // The game goes on without the file, as before; the problem is logged where it can be seen
            // (the editor's warm-up reads ahead silently: Play reports it).
            if (dialog || ex.Line > 0)
                GameLog.Add(GameErrorReport.From(ex, "Reading XML", GameErrorSeverity.Warning));
            return null;
        }
    }

    /// <summary>
    /// The file's XML (parsed once while its bytes do not change). A <see cref="GameDataException"/> when the file
    /// does not exist or is not valid XML, naming the file and the line and column of the error.
    /// </summary>
    public static XmlDocument ReadXmlDocument(string path, string file = "", bool pIgnoreComments = true)
    {
        string fullPath = PathCase.Resolve(Path.Combine(path, file));
        string key0 = GameFiles.Key(fullPath);

        if (!GameFiles.FileExists(fullPath))
            throw new GameDataException("The file does not exist.", key0);

        // In the editor: a file read before and unchanged since (the same bytes in GameFiles) is not parsed again
        // (object sets, trigger templates and models are read on every start; the game only reads them).
        var bytes = GameFiles.ReadAllBytes(fullPath);
        var key = (key0, pIgnoreComments);
        if (bytes != null && ParsedCache.TryGetValue(key, out var cached) && ReferenceEquals(cached.Bytes, bytes))
        {
            return cached.Document;
        }
        var text0 = GameFiles.ReadAllText(fullPath) ?? string.Empty;
        XmlDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text0), new XmlReaderSettings { IgnoreComments = pIgnoreComments });
            document = new XmlDocument();
            document.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new GameDataException("The file is not valid XML: " + ex.Message, key0, line: ex.LineNumber, column: ex.LinePosition, inner: ex);
        }
        XmlSource.Register(document, key0, text0);
        if (bytes != null)
        {
            ParsedCache[key] = (bytes, document);
        }

        return document;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, bool), (byte[] Bytes, XmlDocument Document)> ParsedCache =
        new(new PathKeyComparer());

    private sealed class PathKeyComparer : IEqualityComparer<(string, bool)>
    {
        public bool Equals((string, bool) a, (string, bool) b) => a.Item2 == b.Item2 && string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, bool) k) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item1), k.Item2);
    }

    /// <summary>The parsed files are dropped (another project).</summary>
    public static void ClearParsedCache() => ParsedCache.Clear();
}
