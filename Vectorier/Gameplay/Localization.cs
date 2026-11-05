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

namespace Vectorier.Gameplay
{
    /// <summary>
    /// Texts of localization/localization_all.xml (English), as Vector's LocalizationManager reads them: each text is
    /// an element named by its key with one attribute per language (eng="Jump").
    /// </summary>
    public static class Localization
    {
        const string Language = "eng";

        static Dictionary<string, string>? _texts;

        static Dictionary<string, string> Texts => _texts ??= Load();

        /// <summary>Another project (or the file changed): read again on first use.</summary>
        public static void Reset() => _texts = null;

        static Dictionary<string, string> Load()
        {
            var texts = new Dictionary<string, string>();
            try
            {
                string path = Path.Combine(VectorPaths.Localization, "localization_all.xml");
                var data = GameFiles.ReadAllBytes(path);
                if (data == null)
                    return texts;
                var doc = new XmlDocument();
                doc.Load(new MemoryStream(data));
                var root = doc["log"] ?? doc.DocumentElement;
                if (root == null)
                    return texts;
                foreach (XmlNode node in root.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.Element && node.Attributes?[Language]?.Value is string value)
                        texts[node.Name] = value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("localization_all.xml: " + ex.Message);
            }
            return texts;
        }

        /// <summary>The English text, or the key itself when there is none (GetTranslationOrKey).</summary>
        public static string TranslationOrKey(string key) =>
            !string.IsNullOrEmpty(key) && Texts.TryGetValue(key, out var text) && text.Length > 0 ? text : key;

        /// <summary>The English text, or "%eng_key%" when there is none (GetTranslation).</summary>
        public static string Translation(string key) =>
            !string.IsNullOrEmpty(key) && Texts.TryGetValue(key, out var text) && text.Length > 0 ? text : $"%{Language}_{key}%";
    }
}
