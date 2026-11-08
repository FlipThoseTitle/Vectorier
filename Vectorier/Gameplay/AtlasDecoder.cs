#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Vectorier.Gameplay.Rl;


namespace Vectorier.Gameplay;


public static class AtlasDecoder
{
    public class AtlasSprite
    {
        public string Name = "";

        // Area inside the atlas texture
        public Vectorier.Gameplay.Rl.Rectangle Source;

        // Final untrimmed sprite size
        public Vector2 RealSize;

        // Where the cropped image should be placed inside the real sprite canvas
        public Vectorier.Gameplay.Rl.Rectangle ResultBox;

        public bool Rotated;
    }

    public class Atlas
    {
        public Texture2D Texture;
        public List<AtlasSprite> Sprites = new();

        public AtlasSprite this[int frame] => Sprites[frame];

        public int Count => Sprites.Count;

        public void Draw(
            int frame,
            Vector2 position,
            Vector2 scale,
            Color tint,
            Vector2? pivot = null,
            float width = -1,
            float height = -1,
            Matrix? matrix = null
        )
        {
            Draw(Sprites[frame], position, scale, tint, pivot, matrix, width, height);
        }

        public void Draw(
            AtlasSprite sprite,
            Vector2 position,
            Vector2 scale,
            Color tint,
            Vector2? pivot = null,
            Matrix? externalMatrix = null,
            float width = -1,
            float height = -1
        )
        {
            if (externalMatrix != null)
            {
                DrawTextureAffine(Texture, sprite, position, scale, externalMatrix.Value, tint, pivot);
            }
            else
            {
                var matrix = new Matrix()
                {
                    A = width == -1 ? sprite.RealSize.X : width,
                    B = 0,
                    C = 0,
                    D = height == -1 ? sprite.RealSize.Y : height
                };

                DrawTextureAffine(Texture, sprite, position, scale, matrix, tint, pivot);
            }
        }

        public static void DrawTextureAffine(
            Texture2D texture,
            AtlasSprite sprite,
            Vector2 worldPosition,
            Vector2 worldScale,
            Matrix matrix,
            Color tint,
            Vector2? pivot = null,
            float rotationDegrees = 0f
        )
        {

            Vectorier.Gameplay.Rl.Rectangle source = sprite.Source;

            // The sheet's real size (a texture read mid-game had none when the atlas was made); no image yet: nothing.
            var sheet = Raylib.TextureImage(texture.Id);
            if (sheet == null) return;
            float texW = sheet is Avalonia.Media.Imaging.Bitmap sb ? sb.PixelSize.Width : (float)sheet.Size.Width;
            float texH = sheet is Avalonia.Media.Imaging.Bitmap sb2 ? sb2.PixelSize.Height : (float)sheet.Size.Height;
            float u0 = source.X / texW;
            float v0 = source.Y / texH;
            float u1 = (source.X + source.Width) / texW;
            float v1 = (source.Y + source.Height) / texH;

            float realW = sprite.RealSize.X;
            float realH = sprite.RealSize.Y;

            float x = sprite.ResultBox.X;
            float y = sprite.ResultBox.Y;
            float w = sprite.ResultBox.Width;
            float h = sprite.ResultBox.Height;

            if (!matrix.IsIdentity())
            {
                matrix.A /= realW;
                matrix.B /= realW;
                matrix.C /= realH;
                matrix.D /= realH;
            }

            Vector2 normalizedPivot = pivot ?? Vector2.Zero;

            Vector2 localPivot = new Vector2(
                normalizedPivot.X * realW,
                normalizedPivot.Y * realH
            );

            Vector2 p0 = matrix.TransformPoint(x - localPivot.X, y - localPivot.Y);
            Vector2 p1 = matrix.TransformPoint(x + w - localPivot.X, y - localPivot.Y);
            Vector2 p2 = matrix.TransformPoint(x + w - localPivot.X, y + h - localPivot.Y);
            Vector2 p3 = matrix.TransformPoint(x - localPivot.X, y + h - localPivot.Y);

            p0 *= worldScale;
            p1 *= worldScale;
            p2 *= worldScale;
            p3 *= worldScale;

            if (rotationDegrees != 0f)
            {
                float radians = rotationDegrees * Raylib.DEG2RAD;
                float cos = MathF.Cos(radians);
                float sin = MathF.Sin(radians);

                p0 = RotateAround(p0, Vector2.Zero, cos, sin);
                p1 = RotateAround(p1, Vector2.Zero, cos, sin);
                p2 = RotateAround(p2, Vector2.Zero, cos, sin);
                p3 = RotateAround(p3, Vector2.Zero, cos, sin);
            }

            p0 += worldPosition;
            p1 += worldPosition;
            p2 += worldPosition;
            p3 += worldPosition;

            // Off camera (corners as actually drawn, rotation and skew included): nothing to draw.
            if (!Runners.ImageRendering.IsQuadVisible(p0, p1, p2, p3))
            {
                return;
            }


            Rlgl.SetTexture(texture.Id);
            Rlgl.Begin(DrawMode.Quads);

            Rlgl.Color4ub(tint.R, tint.G, tint.B, tint.A);

            if (!sprite.Rotated)
            {
                Rlgl.TexCoord2f(u0, v0);
                Rlgl.Vertex2f(p0.X, p0.Y);

                Rlgl.TexCoord2f(u1, v0);
                Rlgl.Vertex2f(p1.X, p1.Y);

                Rlgl.TexCoord2f(u1, v1);
                Rlgl.Vertex2f(p2.X, p2.Y);

                Rlgl.TexCoord2f(u0, v1);
                Rlgl.Vertex2f(p3.X, p3.Y);
            }
            else
            {
                Rlgl.TexCoord2f(u1, v0);
                Rlgl.Vertex2f(p0.X, p0.Y);

                Rlgl.TexCoord2f(u1, v1);
                Rlgl.Vertex2f(p1.X, p1.Y);

                Rlgl.TexCoord2f(u0, v1);
                Rlgl.Vertex2f(p2.X, p2.Y);

                Rlgl.TexCoord2f(u0, v0);
                Rlgl.Vertex2f(p3.X, p3.Y);
            }

            Rlgl.End();
            Rlgl.SetTexture(0);
        }

        private static Vector2 RotateAround(
            Vector2 point,
            Vector2 pivot,
            float cos,
            float sin
        )
        {
            Vector2 v = point - pivot;

            return new Vector2(
                pivot.X + v.X * cos - v.Y * sin,
                pivot.Y + v.X * sin + v.Y * cos
            );
        }
    }

    private static Dictionary<string, Texture2D> atlasCache = new Dictionary<string, Texture2D>();

    /// <summary>The editor's addition: the textures of the previous game are gone.</summary>
    public static void ClearCache()
    {
        atlasCache.Clear();
        lock (Pending) Pending.Clear();
    }

    private static readonly Dictionary<string, uint> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A texture read while the game runs: atlases waiting for it get their image.</summary>
    public static void Resolve(string name, Avalonia.Media.IImage image)
    {
        if (image == null) return;
        lock (Pending)
        {
            if (Pending.Remove(name, out uint id))
                Raylib.SetTextureImage(id, image);
        }
    }

    public static Atlas Decode(string atlasPath, string imagePath)
    {
        string atlasText = GameFiles.ReadAllText(atlasPath) ?? string.Empty;

        Texture2D texture;

        if (atlasCache.ContainsKey(imagePath))
        {
            texture = atlasCache[imagePath];
        }
        else
        {
            // The atlas image is the editor's bitmap of the animated texture.
            string name = Path.GetFileNameWithoutExtension(imagePath);
            var image = GameFiles.TextureProvider?.Invoke(name);
            if (image == null)
            {
                // Not loaded by the editor yet: drawn once it is (an effect started mid-game shows a moment later).
                texture = Raylib.RegisterPendingTexture();
                lock (Pending) Pending[name] = texture.Id;
                GameFiles.TextureMissing?.Invoke(name);
            }
            else
            {
                texture = Raylib.RegisterTexture(image);
            }

            atlasCache[imagePath] = texture;
        }

        List<AtlasSprite> sprites;

        if (atlasText.TrimStart().StartsWith("{"))
            sprites = ParseJson(atlasText);
        else
            sprites = ParsePlist(atlasText);

        return new Atlas
        {
            Texture = texture,
            Sprites = sprites
        };
    }

    private static List<AtlasSprite> ParseJson(string json)
    {
        // Source-generated (no reflection), so it keeps working in trimmed builds.
        JsonRoot? root = JsonSerializer.Deserialize(json, AtlasJsonContext.Default.JsonRoot);

        if (root == null || root.frames == null)
            throw new Exception("Invalid atlas JSON.");

        List<AtlasSprite> result = new();

        foreach (JsonFrame frame in root.frames)
        {
            bool rotated = frame.rotated;

            int packedWidth = frame.frame.w;
            int packedHeight = frame.frame.h;

            int width = rotated ? frame.frame.h : frame.frame.w;
            int height = rotated ? frame.frame.w : frame.frame.h;

            int realWidth = frame.sourceSize.w;
            int realHeight = frame.sourceSize.h;

            AtlasSprite sprite = new AtlasSprite
            {
                Name = frame.filename,

                Source = new Vectorier.Gameplay.Rl.Rectangle(
                    frame.frame.x,
                    frame.frame.y,
                    packedWidth,
                    packedHeight
                ),

                RealSize = new Vector2(
                    realWidth,
                    realHeight
                ),

                Rotated = rotated,

                ResultBox = new Vectorier.Gameplay.Rl.Rectangle(
                    (realWidth - width) / 2f,
                    (realHeight - height) / 2f,
                    width,
                    height
                )
            };

            result.Add(sprite);
        }

        SortAtlas(result);

        return result;
    }

    private static List<AtlasSprite> ParsePlist(string xml)
    {
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xml);

        XmlNode plist = doc.DocumentElement!;
        XmlNode dict = plist["dict"]!;

        Dictionary<string, object> root = PlistToDictionary(dict);

        Dictionary<string, object> frames =
            (Dictionary<string, object>)root["frames"];

        bool format3 = false;

        if (root.ContainsKey("metadata"))
        {
            var metadata = (Dictionary<string, object>)root["metadata"];

            if (metadata.ContainsKey("format"))
                format3 = Convert.ToInt32(metadata["format"]) == 3;
        }

        List<AtlasSprite> result = new();

        foreach (var kv in frames)
        {
            string name = kv.Key;

            Dictionary<string, object> frame =
                (Dictionary<string, object>)kv.Value;

            if (format3)
            {
                frame = new Dictionary<string, object>
        {
            { "frame", frame["textureRect"] },
            { "rotated", frame["textureRotated"] },
            { "sourceSize", frame["spriteSourceSize"] },
            { "offset", frame["spriteOffset"] }
        };
            }

            List<float> rect = ParseList((string)frame["frame"]);

            bool rotated = Convert.ToBoolean(frame["rotated"]);

            int posX = (int)rect[0];
            int posY = (int)rect[1];

            float logicalWidth = rect[2];
            float logicalHeight = rect[3];

            float packedWidth = rotated ? logicalHeight : logicalWidth;
            float packedHeight = rotated ? logicalWidth : logicalHeight;

            List<float> real = ParseList((string)frame["sourceSize"]);

            int realWidth = (int)real[0];
            int realHeight = (int)real[1];

            List<float> offset = ParseList((string)frame["offset"]);

            int offsetX = (int)offset[0];
            int offsetY = (int)offset[1];

            Vectorier.Gameplay.Rl.Rectangle resultBox = CenterBox(
                logicalWidth,
                logicalHeight,
                realWidth,
                realHeight,
                offsetX,
                offsetY
            );

            result.Add(new AtlasSprite
            {
                Name = name,

                // Physical rectangle in the atlas
                Source = new Vectorier.Gameplay.Rl.Rectangle(
                    posX,
                    posY,
                    packedWidth,
                    packedHeight
                ),

                // Original untrimmed sprite size
                RealSize = new Vector2(
                    realWidth,
                    realHeight
                ),

                Rotated = rotated,

                // Logical unrotated placement inside 100x100 canvas
                ResultBox = resultBox
            });
        }

        SortAtlas(result);

        return result;
    }

    private static Vectorier.Gameplay.Rl.Rectangle CenterBox(
        float width,
        float height,
        float realWidth,
        float realHeight,
        float offsetX,
        float offsetY
    )
    {
        return new Vectorier.Gameplay.Rl.Rectangle(
            (realWidth - width) / 2f + offsetX,
            (realHeight - height) / 2f - offsetY,
            width,
            height
        );
    }

    private static List<float> ParseList(string s)
    {
        List<float> result = new();

        string cleaned = s
            .Replace("{", "")
            .Replace("}", "");

        string[] parts = cleaned.Split(',');

        foreach (string p in parts)
        {
            if (float.TryParse(
                p.Trim(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out float value
            ))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static Dictionary<string, object> PlistToDictionary(XmlNode node)
    {
        Dictionary<string, object> result = new();

        XmlNodeList children = node.ChildNodes;

        for (int i = 0; i < children.Count; i += 2)
        {
            XmlNode key = children[i];
            XmlNode val = children[i + 1];

            result[key.InnerText] = ParsePlistValue(val)!;
        }

        return result;
    }

    private static object? ParsePlistValue(XmlNode node)
    {
        return node.Name switch
        {
            "string" => node.InnerText,
            "integer" => int.Parse(node.InnerText, System.Globalization.CultureInfo.InvariantCulture),
            "true" => true,
            "false" => false,
            "dict" => PlistToDictionary(node),
            _ => null
        };
    }

    private static void SortAtlas(List<AtlasSprite> sprites)
    {
        sprites.Sort((a, b) =>
            new NaturalNameComparer().Compare(a.Name, b.Name)
        );
    }

    private class NaturalNameComparer : IComparer<string>
    {
        public int Compare(string? a, string? b)
        {
            if (a == null || b == null)
                return string.Compare(a, b, StringComparison.Ordinal);

            MatchCollection ma = Regex.Matches(a, @"\d+");
            MatchCollection mb = Regex.Matches(b, @"\d+");

            int count = Math.Min(ma.Count, mb.Count);

            for (int i = 0; i < count; i++)
            {
                int na = int.Parse(ma[i].Value, System.Globalization.CultureInfo.InvariantCulture);
                int nb = int.Parse(mb[i].Value, System.Globalization.CultureInfo.InvariantCulture);

                int compare = na.CompareTo(nb);

                if (compare != 0)
                    return compare;
            }

            return string.Compare(a, b, StringComparison.Ordinal);
        }
    }

    internal class JsonRoot
    {
        public JsonFrame[] frames { get; set; } = [];
    }

    internal class JsonFrame
    {
        public string filename { get; set; } = "";
        public bool rotated { get; set; }
        public JsonRect frame { get; set; } = new();
        public JsonRect sourceSize { get; set; } = new();
    }

    internal class JsonRect
    {
        public int x { get; set; }
        public int y { get; set; }
        public int w { get; set; }
        public int h { get; set; }
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(AtlasDecoder.JsonRoot))]
internal partial class AtlasJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
