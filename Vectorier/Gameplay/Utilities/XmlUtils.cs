// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Vectorier.Gameplay.Utilities
{
    public static class XmlUtils
    {
        public enum OpenXmlType
        {
            Normal = 0,
            ForcedResourced = 1,
            ForcedExternal = 2
        }

        public const string Comment = "#comment";

        public static bool IsNodeComment(XmlNode node)
        {
            return node.NodeType == XmlNodeType.Comment;
        }

        public static int ParseInt(this XmlAttribute attr, int defVal = 0)
        {
            if (attr == null || !int.TryParse(attr.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
            {
                return defVal;
            }
            return result;
        }

        public static long ParseLong(XmlAttribute attr, long defVal = 0L)
        {
            if (attr == null || !long.TryParse(attr.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long result))
            {
                return defVal;
            }
            return result;
        }

        public static uint ParseUint(XmlAttribute attr, uint defVal = 0u)
        {
            if (attr == null || !uint.TryParse(attr.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint result))
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

        public static double ParseDouble(this XmlAttribute attr, double defVal = 0.0)
        {
            if (attr == null || !double.TryParse(attr.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
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

        public static T ParseEnum<T>(XmlAttribute attr, T defVal)
        {
            if (attr == null)
            {
                return defVal;
            }
            try
            {
                return (T)Enum.Parse(typeof(T), attr.Value, true);
            }
            catch
            {
                return defVal;
            }
        }

        public static XmlDocument OpenXMLDocumentFromBytes(byte[] pBytes, bool pIgnoreComments = true)
        {
            XmlReaderSettings xmlReaderSettings = new XmlReaderSettings();
            xmlReaderSettings.IgnoreComments = pIgnoreComments;
            try
            {
                MemoryStream stream = new MemoryStream(pBytes);
                using (XmlReader p_reader = XmlReader.Create(stream, xmlReaderSettings))
                {
                    return OpenXMLDocument(p_reader);
                }
            }
            catch
            {
                return null;
            }
        }

        public static T DeserializeXMLDocument<T>(string path, string file = "", OpenXmlType openType = OpenXmlType.Normal, bool pIgnoreComments = true)
        {
            return default(T);
        }

        public static XmlDocument OpenXMLDocument(string path, string file = "", OpenXmlType openType = OpenXmlType.Normal, bool pIgnoreComments = true, bool dialog = true)
        {
            string fullPath = PathCase.Resolve(Path.Combine(path, file));
            var xmlReaderSettings = new XmlReaderSettings();
            xmlReaderSettings.IgnoreComments = pIgnoreComments;
            XmlDocument document = null;

            if (!GameFiles.FileExists(fullPath))
            {
                if (dialog)
                    Console.WriteLine("FILE DOESNT EXIST: " + fullPath, true, true);

                return null;
            }
            // In the editor: a file read before and unchanged since (the same bytes in GameFiles) is not parsed again
            // (object sets, trigger templates and models are read on every start; the game only reads them).
            var bytes = GameFiles.ReadAllBytes(fullPath);
            var key = (GameFiles.Key(fullPath), pIgnoreComments);
            if (bytes != null && ParsedCache.TryGetValue(key, out var cached) && ReferenceEquals(cached.Bytes, bytes))
            {
                return cached.Document;
            }
            var text0 = GameFiles.ReadAllText(fullPath);
            document = OpenXMLDocFromString(text0, xmlReaderSettings);
            if (bytes != null && document != null)
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

        public static XmlElement OpenXMLElementFromString(string content, bool pIgnoreComments = true)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }
            XmlDocument doc = OpenXMLDocumentFromString(content);
            if (doc != null)
            {
                return doc.DocumentElement;
            }
            return null;
        }

        public static XmlDocument OpenXMLDocumentFromString(string content, bool pIgnoreComments = true)
        {
            using (XmlReader p_reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings()))
            {
                XmlDocument xmlDocument = OpenXMLDocument(p_reader);
                if (xmlDocument == null)
                {
                    Console.WriteLine("Error read XML", true);
                }
                return xmlDocument;
            }
        }

        private static XmlDocument OpenXMLDocFromString(string pData, XmlReaderSettings pSettings)
        {
            using (XmlReader p_reader = XmlReader.Create(new StringReader(pData), pSettings))
            {
                XmlDocument xmlDocument = OpenXMLDocument(p_reader);
                if (xmlDocument == null)
                {
                    Console.WriteLine("Error read XML", true);
                }
                return xmlDocument;
            }
        }

        private static XmlDocument OpenXMLDocument(XmlReader pReader)
        {
            try
            {
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.Load(pReader);
                return xmlDocument;
            }
            catch
            {
                return null;
            }
        }

        public static void CopyXmlFromResources(string pFrom, string pTo)
        {
        }

        public static void CopyXmlAndTrimSpaces(string pFrom, string pTo)
        {
        }

        public static void TrimSpacesFromXmlInDirectory(string pPath, bool pRecursively = false)
        {
        }

        public static bool IsFileValid(string pPath)
        {
            return false;
        }
    }
}
