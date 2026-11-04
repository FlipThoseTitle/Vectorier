// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            if (attr == null || !int.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                return defVal;
            }
            return result;
        }

        public static long ParseLong(XmlAttribute attr, long defVal = 0L)
        {
            if (attr == null || !long.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
            {
                return defVal;
            }
            return result;
        }

        public static uint ParseUint(XmlAttribute attr, uint defVal = 0u)
        {
            if (attr == null || !uint.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint result))
            {
                return defVal;
            }
            return result;
        }

        public static float ParseFloat(this XmlAttribute attr, float defVal = 0f)
        {
            if (attr == null || !float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            {
                return defVal;
            }
            return result;
        }

        public static double ParseDouble(this XmlAttribute attr, double defVal = 0.0)
        {
            if (attr == null || !double.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                return defVal;
            }
            return result;
        }

        public static bool ParseBool(this XmlAttribute attr, bool defVal = false)
        {
            if (attr == null || !int.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
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
            // Read beforehand by the editor (see GameplayFiles), as File.ReadAllText reads it in the game.
            string fullPath = string.IsNullOrEmpty(file) ? path : path + "/" + file;
            var xmlReaderSettings = new XmlReaderSettings();
            xmlReaderSettings.IgnoreComments = pIgnoreComments;
            XmlDocument document = null;

            var text0 = GameplayFiles.GetText(fullPath);
            if (text0 == null)
            {
                return null;
            }
            document = OpenXMLDocFromString(text0, xmlReaderSettings);

            return document;
        }

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
