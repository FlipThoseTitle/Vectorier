using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;

namespace Vectorier.Services.Assets;

/// <summary>One frame of a sprite sheet (.plist / .json), same meaning as the game's AtlasDecoder.</summary>
public sealed class AtlasFrame
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Area inside the sheet (already rotated when <see cref="Rotated"/>).</summary>
    public Rect Source { get; init; }

    /// <summary>Untrimmed sprite size.</summary>
    public Size RealSize { get; init; }

    /// <summary>Where the (unrotated) cropped image sits inside the untrimmed canvas.</summary>
    public Rect ResultBox { get; init; }

    /// <summary>The frame is stored rotated 90° clockwise in the sheet.</summary>
    public bool Rotated { get; init; }
}

/// <summary>Parses TexturePacker .plist (formats 2 and 3) and JSON atlases, sorted naturally by frame name.</summary>
public static class AtlasParser
{
    public static List<AtlasFrame> Parse(string text)
    {
        var frames = text.TrimStart().StartsWith('{') ? ParseJson(text) : ParsePlist(text);
        frames.Sort((a, b) => NaturalCompare(a.Name, b.Name));
        return frames;
    }

    private static List<AtlasFrame> ParsePlist(string xml)
    {
        var doc = XDocument.Parse(xml.TrimStart('﻿'));
        var dict = doc.Root?.Element("dict") ?? throw new FormatException("Invalid plist.");
        var root = ToDictionary(dict);
        var frames = (Dictionary<string, object?>)root["frames"]!;

        bool format3 = root.TryGetValue("metadata", out var meta) && meta is Dictionary<string, object?> metadata &&
                       metadata.TryGetValue("format", out var format) && Convert.ToInt32(format) == 3;

        var result = new List<AtlasFrame>();
        foreach (var (name, value) in frames)
        {
            if (value is not Dictionary<string, object?> frame)
                continue;

            if (format3)
            {
                frame = new Dictionary<string, object?>
                {
                    ["frame"] = frame.GetValueOrDefault("textureRect"),
                    ["rotated"] = frame.GetValueOrDefault("textureRotated"),
                    ["sourceSize"] = frame.GetValueOrDefault("spriteSourceSize"),
                    ["offset"] = frame.GetValueOrDefault("spriteOffset"),
                };
            }

            var rect = ParseList(frame.GetValueOrDefault("frame") as string);
            bool rotated = frame.GetValueOrDefault("rotated") is true;
            if (rect.Count < 4)
                continue;

            double posX = rect[0], posY = rect[1], logicalW = rect[2], logicalH = rect[3];
            double packedW = rotated ? logicalH : logicalW;
            double packedH = rotated ? logicalW : logicalH;

            var real = ParseList(frame.GetValueOrDefault("sourceSize") as string);
            double realW = real.Count > 0 ? real[0] : logicalW;
            double realH = real.Count > 1 ? real[1] : logicalH;

            var offset = ParseList(frame.GetValueOrDefault("offset") as string);
            double offsetX = offset.Count > 0 ? offset[0] : 0;
            double offsetY = offset.Count > 1 ? offset[1] : 0;

            result.Add(new AtlasFrame
            {
                Name = name,
                Source = new Rect(posX, posY, packedW, packedH),
                RealSize = new Size(realW, realH),
                Rotated = rotated,
                ResultBox = new Rect((realW - logicalW) / 2 + offsetX, (realH - logicalH) / 2 - offsetY, logicalW, logicalH),
            });
        }
        return result;
    }

    private static List<AtlasFrame> ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<AtlasFrame>();
        if (!doc.RootElement.TryGetProperty("frames", out var frames))
            return result;

        IEnumerable<(string Name, JsonElement Value)> entries = frames.ValueKind == JsonValueKind.Array
            ? frames.EnumerateArray().Select(f => (f.TryGetProperty("filename", out var n) ? n.GetString() ?? "" : "", f))
            : frames.EnumerateObject().Select(p => (p.Name, p.Value));

        foreach (var (name, f) in entries)
        {
            var frame = f.GetProperty("frame");
            bool rotated = f.TryGetProperty("rotated", out var r) && r.ValueKind == JsonValueKind.True;
            double x = frame.GetProperty("x").GetDouble(), y = frame.GetProperty("y").GetDouble();
            double w = frame.GetProperty("w").GetDouble(), h = frame.GetProperty("h").GetDouble();
            double width = rotated ? h : w, height = rotated ? w : h;
            var source = f.TryGetProperty("sourceSize", out var s) ? s : frame;
            double realW = source.GetProperty("w").GetDouble(), realH = source.GetProperty("h").GetDouble();

            result.Add(new AtlasFrame
            {
                Name = name,
                Source = new Rect(x, y, w, h),
                RealSize = new Size(realW, realH),
                Rotated = rotated,
                ResultBox = new Rect((realW - width) / 2, (realH - height) / 2, width, height),
            });
        }
        return result;
    }

    private static Dictionary<string, object?> ToDictionary(XElement dict)
    {
        var result = new Dictionary<string, object?>();
        var children = dict.Elements().ToList();
        for (int i = 0; i + 1 < children.Count; i += 2)
            result[children[i].Value] = ParseValue(children[i + 1]);
        return result;
    }

    private static object? ParseValue(XElement node) => node.Name.LocalName switch
    {
        "string" => node.Value,
        "integer" => int.TryParse(node.Value, out int i) ? i : 0,
        "real" => double.TryParse(node.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0,
        "true" => true,
        "false" => false,
        "dict" => ToDictionary(node),
        _ => null,
    };

    private static List<double> ParseList(string? text)
    {
        var result = new List<double>();
        if (string.IsNullOrEmpty(text))
            return result;
        foreach (var part in text.Replace("{", "").Replace("}", "").Split(','))
        {
            if (double.TryParse(part.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                result.Add(value);
        }
        return result;
    }

    private static int NaturalCompare(string a, string b)
    {
        var ma = Regex.Matches(a, @"\d+");
        var mb = Regex.Matches(b, @"\d+");
        int count = Math.Min(ma.Count, mb.Count);
        for (int i = 0; i < count; i++)
        {
            if (long.TryParse(ma[i].Value, out long na) && long.TryParse(mb[i].Value, out long nb))
            {
                int compare = na.CompareTo(nb);
                if (compare != 0)
                    return compare;
            }
        }
        return string.CompareOrdinal(a, b);
    }
}
