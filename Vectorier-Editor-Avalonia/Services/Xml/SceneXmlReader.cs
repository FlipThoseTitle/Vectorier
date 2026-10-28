using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Vectorier_Editor_Avalonia.Models.Scene;

namespace Vectorier_Editor_Avalonia.Services.Xml;

/// <summary>
/// Converts Vector XML into the editor's scene model. Unknown attributes and elements are kept so that
/// saving reproduces everything the editor does not understand.
/// </summary>
public sealed class SceneXmlReader
{
    public const string CommentPrefix = "Vectorier.Comment";

    private readonly DefinitionTable? _definitions;
    private readonly HashSet<string> _building = new(StringComparer.Ordinal);

    public SceneXmlReader(DefinitionTable? definitions)
    {
        _definitions = definitions;
    }

    public List<string> Warnings { get; } = new();

    // ================= DOCUMENTS ================= //

    /// <summary>Reads the &lt;Sets&gt; of a level without parsing the rest (used to resolve definitions first).</summary>
    public static (List<string> City, List<string> Ground) ReadSets(XDocument doc)
    {
        var city = new List<string>();
        var ground = new List<string>();
        var sets = doc.Root?.Element("Sets");
        if (sets != null)
        {
            foreach (var set in sets.Elements())
            {
                string? file = set.Attribute("FileName")?.Value;
                if (string.IsNullOrWhiteSpace(file))
                    continue;
                if (set.Name.LocalName == "City") city.Add(file);
                else if (set.Name.LocalName == "Ground") ground.Add(file);
            }
        }
        return (city, ground);
    }

    public SceneDocument ReadLevel(XDocument doc, string levelName)
    {
        var scene = new SceneDocument { LevelName = levelName };
        var root = doc.Root ?? throw new InvalidOperationException("The XML has no root element.");
        var settings = new LevelSettings
        {
            CitySets = new List<string>(),
            GroundSets = new List<string>(),
            LibrarySets = new List<string>(),
            CommonModels = new List<LevelModelDefinition>(),
            HunterModels = new List<LevelModelDefinition>(),
            MusicName = string.Empty,
            CoinValue = 0,
        };
        bool hasCoins = false;

        foreach (var child in root.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Sets":
                    foreach (var set in child.Elements())
                    {
                        string file = set.Attribute("FileName")?.Value ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(file))
                            continue;
                        switch (set.Name.LocalName)
                        {
                            case "City": settings.CitySets.Add(file); break;
                            case "Ground": settings.GroundSets.Add(file); break;
                            case "Library": settings.LibrarySets.Add(file); break;
                        }
                    }
                    break;

                case "Music":
                    settings.MusicName = child.Attribute("Name")?.Value ?? string.Empty;
                    settings.MusicVolume = Num.Parse(child.Attribute("Volume")?.Value, 0.3);
                    break;

                case "Models":
                    string variant = child.Attribute("Variant")?.Value ?? "CommonMode";
                    var models = child.Elements("Model").Select(m =>
                    {
                        var def = new LevelModelDefinition();
                        foreach (var a in m.Attributes())
                            def.Attributes.Add(new KeyValuePair<string, string>(a.Name.LocalName, a.Value));
                        return def;
                    }).ToList();
                    if (variant == "HunterMode")
                        settings.HunterModels.AddRange(models);
                    else
                        settings.CommonModels.AddRange(models);
                    break;

                case "Coins":
                    settings.CoinValue = Num.ParseInt(child.Attribute("Value")?.Value);
                    hasCoins = true;
                    break;

                case "Objects" when (string?)child.Attribute("Name") == "Money" && !child.HasElements:
                    // Regenerated with <Coins>.
                    break;

                case "Track":
                    ReadTrack(child, scene, settings);
                    break;

                default:
                    settings.ExtraRootElements.Add(new XElement(child));
                    break;
            }
        }

        if (!hasCoins)
            settings.CoinValue = 50;

        scene.Settings = settings;
        return scene;
    }

    private void ReadTrack(XElement track, SceneDocument scene, LevelSettings settings)
    {
        foreach (var layerObject in track.Elements("Object"))
        {
            string factor = layerObject.Attribute("Factor")?.Value ?? "1";
            settings.SourceFactors.Add(factor);
            string layer = Layers.GetLayerName(factor);

            var content = layerObject.Element("Content");
            if (content == null)
                continue;

            foreach (var element in ReadContent(content))
            {
                element.Layer = layer;
                scene.Insert(element, null);
            }
        }
    }

    /// <summary>Reads an objects/buildings file as a scene (every definition becomes a top-level Object).</summary>
    public List<SceneElement> ReadObjectsFile(XDocument doc)
    {
        var list = new List<SceneElement>();
        var objects = doc.Root?.Element("Objects");
        if (objects == null)
            return list;
        foreach (var obj in objects.Elements("Object"))
        {
            var element = ReadObject(obj, resolveReference: false);
            list.Add(element);
        }
        return list;
    }

    // ================= CONTENT ================= //

    public List<SceneElement> ReadContent(XElement content)
    {
        var list = new List<SceneElement>();
        foreach (var node in content.Nodes())
        {
            if (node is XElement element)
            {
                var parsed = ReadElement(element);
                if (parsed != null)
                    list.Add(parsed);
            }
            else if (node is XComment comment && TryReadComment(comment, out var commentElement))
            {
                list.Add(commentElement);
            }
        }
        return list;
    }

    public SceneElement? ReadElement(XElement x)
    {
        try
        {
            return x.Name.LocalName switch
            {
                "Object" => ReadObject(x, resolveReference: true),
                "Image" => ReadImage(x),
                "Trigger" => ReadTrigger(x),
                "Area" => ReadArea(x),
                "Platform" => ReadPlatform(x),
                "Trapezoid" => ReadTrapezoid(x),
                "Camera" => ReadCamera(x),
                "Spawn" => ReadSpawn(x),
                "Model" => ReadModel(x),
                "Item" => ReadItem(x),
                "Animation" => ReadAnimation(x),
                "Particle" => ReadParticle(x),
                _ => new UnknownElement { Xml = new XElement(x), Name = x.Name.LocalName },
            };
        }
        catch (Exception ex)
        {
            Warnings.Add($"<{x.Name.LocalName}> could not be read and was kept unchanged: {ex.Message}");
            return new UnknownElement { Xml = new XElement(x), Name = x.Name.LocalName };
        }
    }

    // ---- shared parts ----

    private void ReadCommon(SceneElement element, XElement x, AttributeReader reader, Action<XElement>? readStatic = null)
    {
        var properties = x.Element("Properties");
        if (properties != null)
        {
            foreach (var part in properties.Elements())
            {
                switch (part.Name.LocalName)
                {
                    case "Static":
                        foreach (var item in part.Elements())
                        {
                            if (item.Name.LocalName == "Selection")
                            {
                                string variant = item.Attribute("Variant")?.Value ?? "CommonMode";
                                element.Selection = variant == "HunterMode" ? ModeSelection.HunterMode : ModeSelection.CommonMode;
                            }
                            else if (readStatic != null && item.Name.LocalName is "StartColor" or "Matrix")
                            {
                                readStatic(item);
                            }
                            else
                            {
                                element.ExtraStatic.Add(new XElement(item));
                            }
                        }
                        break;

                    case "Dynamic":
                        foreach (var transformation in part.Elements("Transformation"))
                            element.Dynamics.Add(ReadTransformation(transformation));
                        break;

                    default:
                        element.ExtraProperties.Add(new XElement(part));
                        break;
                }
            }
        }

        foreach (var child in x.Elements())
        {
            string name = child.Name.LocalName;
            if (name == "Properties" || name == "Content")
                continue;
            element.ExtraChildren.Add(new XElement(child));
        }

        element.ExtraAttributes = reader.Remaining();
    }

    private static DynamicTransformation ReadTransformation(XElement x)
    {
        var t = new DynamicTransformation { Name = x.Attribute("Name")?.Value ?? "Transform" };

        var move = x.Element("Move");
        if (move != null)
        {
            foreach (var interval in move.Elements("MoveInterval"))
            {
                var m = new DynamicMove
                {
                    Frames = Num.ParseInt(interval.Attribute("FramesToMove")?.Value),
                    Delay = Num.Parse(interval.Attribute("Delay")?.Value),
                };
                foreach (var point in interval.Elements("Point"))
                {
                    double px = Num.Parse(point.Attribute("X")?.Value);
                    double py = Num.Parse(point.Attribute("Y")?.Value);
                    switch (point.Attribute("Name")?.Value)
                    {
                        case "Support": m.SupportX = px; m.SupportY = py; break;
                        case "Finish": m.FinishX = px; m.FinishY = py; break;
                    }
                }
                t.Moves.Add(m);
            }
        }

        foreach (var size in x.Elements("Size"))
        {
            t.Sizes.Add(new DynamicSize
            {
                Frames = Num.ParseInt(size.Attribute("Frames")?.Value),
                FinalWidth = Num.Parse(size.Attribute("FinalWidth")?.Value),
                FinalHeight = Num.Parse(size.Attribute("FinalHeight")?.Value),
            });
        }

        foreach (var rotation in x.Elements("Rotation"))
        {
            var r = new DynamicRotation
            {
                Angle = Num.Parse(rotation.Attribute("Angle")?.Value),
                Frames = Num.ParseInt(rotation.Attribute("Frames")?.Value),
            };
            var anchor = (rotation.Attribute("Anchor")?.Value ?? "0|0").Split('|');
            r.AnchorX = anchor.Length > 0 ? Num.Parse(anchor[0]) : 0;
            r.AnchorY = anchor.Length > 1 ? Num.Parse(anchor[1]) : 0;
            t.Rotations.Add(r);
        }

        foreach (var color in x.Elements("Color"))
        {
            var c = new DynamicColor { Frames = Num.ParseInt(color.Attribute("Frames")?.Value) };
            if (XmlHelpers.TryParseRgba(color.Attribute("ColorStart")?.Value, out var start)) c.Start = start;
            if (XmlHelpers.TryParseRgba(color.Attribute("ColorFinish")?.Value, out var finish)) c.Finish = finish;
            t.Colors.Add(c);
        }

        return t;
    }

    // ---- element types ----

    private ObjectElement ReadObject(XElement x, bool resolveReference)
    {
        var reader = new AttributeReader(x);
        var obj = new ObjectElement
        {
            Name = reader.String("Name") ?? string.Empty,
            PositionWasPresent = reader.Has("X") || reader.Has("Y"),
            X = reader.Double("X"),
            Y = reader.Double("Y"),
        };

        var content = x.Element("Content");
        if (content != null)
        {
            foreach (var child in ReadContent(content))
                obj.AddChild(child);
        }

        ReadCommon(obj, x, reader);

        if (resolveReference && !string.IsNullOrEmpty(obj.Name))
            obj.Reference = ResolveDefinition(obj.Name);

        return obj;
    }

    /// <summary>Finds and builds (once) the definition of a named object.</summary>
    public ObjectDefinition? ResolveDefinition(string name)
    {
        var def = _definitions?.Find(name);
        if (def == null)
            return null;

        BuildDefinition(def);
        return def;
    }

    public void BuildDefinition(ObjectDefinition def)
    {
        if (def.IsBuilt || _building.Contains(def.Name))
            return;

        _building.Add(def.Name);
        try
        {
            var content = def.Xml.Element("Content");
            def.Content = content != null ? ReadContent(content) : new List<SceneElement>();
            def.ComputeBounds();
            def.IsBuilt = true;
        }
        finally
        {
            _building.Remove(def.Name);
        }
    }

    private ImageElement ReadImage(XElement x)
    {
        var reader = new AttributeReader(x);
        var image = new ImageElement
        {
            Name = reader.String("ClassName") ?? string.Empty,
            X = reader.Double("X"),
            Y = reader.Double("Y"),
        };

        bool hasWidth = reader.Has("Width"), hasHeight = reader.Has("Height");
        image.Width = reader.Double("Width");
        image.Height = reader.Double("Height");
        image.AutoSize = !hasWidth || !hasHeight;
        image.NativeWidth = reader.Int("NativeX");
        image.NativeHeight = reader.Int("NativeY");

        int type = reader.Int("Type");
        image.ImageType = Enum.IsDefined(typeof(ImageType), type) ? (ImageType)type : ImageType.None;

        if (reader.Has("Depth"))
        {
            int depth = reader.Int("Depth", -1);
            image.Depth = depth == 0 ? ImageDepth.Front : depth == 1 ? ImageDepth.Back : ImageDepth.Middle;
        }

        if (reader.Has("Color") && XmlHelpers.TryParseRgba(x.Attribute("Color")!.Value, out var attrColor))
        {
            reader.Consume("Color");
            image.Color = attrColor;
        }

        XElement? matrix = null;
        ReadCommon(image, x, reader, item =>
        {
            if (item.Name.LocalName == "StartColor")
            {
                if (XmlHelpers.TryParseRgba(item.Attribute("Color")?.Value, out var color))
                    image.Color = color;
            }
            else if (item.Name.LocalName == "Matrix")
            {
                matrix = item;
            }
        });

        if (matrix != null)
        {
            double a = Num.Parse(matrix.Attribute("A")?.Value, 1);
            double b = Num.Parse(matrix.Attribute("B")?.Value);
            double c = Num.Parse(matrix.Attribute("C")?.Value);
            double d = Num.Parse(matrix.Attribute("D")?.Value, 1);
            double tx = Num.Parse(matrix.Attribute("Tx")?.Value);
            double ty = Num.Parse(matrix.Attribute("Ty")?.Value);

            image.X += tx;
            image.Y += ty;
            image.SetBasis(a, b, c, d);
            image.AutoSize = false;
            image.HasMatrix = true;
        }

        return image;
    }

    private TriggerElement ReadTrigger(XElement x)
    {
        var reader = new AttributeReader(x);
        var trigger = new TriggerElement
        {
            Name = reader.String("Name") ?? string.Empty,
            X = reader.Double("X"),
            Y = reader.Double("Y"),
            Width = reader.Double("Width"),
            Height = reader.Double("Height"),
        };

        var content = x.Element("Content");
        trigger.ContentXml = content != null ? XmlHelpers.FormatFragment(content.Nodes()) : string.Empty;

        ReadCommon(trigger, x, reader);
        return trigger;
    }

    private AreaElement ReadArea(XElement x)
    {
        var reader = new AttributeReader(x);
        var area = new AreaElement
        {
            Name = reader.String("Name") ?? string.Empty,
            X = reader.Double("X"),
            Y = reader.Double("Y"),
            Width = reader.Double("Width"),
            Height = reader.Double("Height"),
        };

        string type = reader.String("Type") ?? "Animation";
        area.AreaType = string.IsNullOrEmpty(type) ? "Animation" : type;

        switch (area.AreaType)
        {
            case "Catch":
                area.Distance = reader.Int("Distance", 300);
                break;
            case "Trick":
                area.ItemName = reader.String("ItemName") ?? string.Empty;
                area.Score = reader.Int("Score", 100);
                break;
            case "Help":
                area.Key = reader.String("Key") ?? "Up";
                area.Description = reader.String("Description") ?? string.Empty;
                break;
        }

        ReadCommon(area, x, reader);
        return area;
    }

    private PlatformElement ReadPlatform(XElement x)
    {
        var reader = new AttributeReader(x);
        var platform = new PlatformElement
        {
            X = reader.Double("X"),
            Y = reader.Double("Y"),
            Width = reader.Double("Width"),
            Height = reader.Double("Height"),
        };
        if (reader.Has("Name"))
            platform.Name = reader.String("Name") ?? string.Empty;
        ReadCommon(platform, x, reader);
        return platform;
    }

    private TrapezoidElement ReadTrapezoid(XElement x)
    {
        var reader = new AttributeReader(x);
        var trapezoid = new TrapezoidElement();
        if (reader.Has("Name"))
            trapezoid.Name = reader.String("Name") ?? string.Empty;

        double xmlX = reader.Double("X");
        double xmlY = reader.Double("Y");
        double width = reader.Double("Width");
        double heightLeft = reader.Double("Height");
        double heightRight = reader.Double("Height1");
        int type = reader.Int("Type", 1);

        double slope = Math.Abs(heightLeft - heightRight);
        double total = Math.Max(heightLeft, heightRight);
        double baseHeight = Math.Min(heightLeft, heightRight);

        // The game places the left edge's top at (X, Y): P2.y = Y + (HeightLeft - HeightRight).
        bool risesToRight = heightRight > heightLeft || (Math.Abs(heightRight - heightLeft) < 1e-9 && type == 1);
        trapezoid.TrapezoidType = risesToRight ? 1 : 2;
        trapezoid.X = xmlX;
        trapezoid.Y = risesToRight ? xmlY - slope : xmlY;
        trapezoid.Width = width;
        trapezoid.BaseHeight = baseHeight;
        trapezoid.Height = total;

        ReadCommon(trapezoid, x, reader);
        return trapezoid;
    }

    private CameraElement ReadCamera(XElement x)
    {
        var reader = new AttributeReader(x);
        var camera = new CameraElement
        {
            Name = "Camera",
            X = reader.Double("X"),
            Y = reader.Double("Y"),
        };
        ReadCommon(camera, x, reader);
        return camera;
    }

    private SpawnElement ReadSpawn(XElement x)
    {
        var reader = new AttributeReader(x);
        var spawn = new SpawnElement
        {
            X = reader.Double("X"),
            Y = reader.Double("Y"),
            Name = reader.String("Name") ?? SpawnElement.DefaultName,
        };
        string? animation = reader.String("Animation");
        spawn.Animation = string.IsNullOrWhiteSpace(animation) ? SpawnElement.DefaultAnimation : animation;
        ReadCommon(spawn, x, reader);
        return spawn;
    }

    private ModelElement ReadModel(XElement x)
    {
        var reader = new AttributeReader(x);
        var model = new ModelElement
        {
            X = reader.Double("X"),
            Y = reader.Double("Y"),
            Name = reader.String("ClassName") ?? string.Empty,
        };
        model.ModelType = reader.Int("Type", 1) == 0 ? ModelType.Physics : ModelType.Static;
        if (reader.Has("LifeTime"))
            model.LifeTime = reader.Int("LifeTime", 10);
        ReadCommon(model, x, reader);
        return model;
    }

    private ItemElement ReadItem(XElement x)
    {
        var reader = new AttributeReader(x);
        var item = new ItemElement
        {
            X = reader.Double("X"),
            Y = reader.Double("Y"),
        };
        item.ItemType = reader.Int("Type") == 1 ? ItemType.Coin : ItemType.Bonus;
        item.Name = item.ItemType == ItemType.Bonus ? "Bonus" : "Coin";
        item.Score = reader.Int("Score", 10);
        item.Radius = reader.Double("Radius", 80);
        item.GroupId = reader.Int("GroupId", 1);
        ReadCommon(item, x, reader);
        return item;
    }

    private AnimationElement ReadAnimation(XElement x)
    {
        var reader = new AttributeReader(x);
        var animation = new AnimationElement
        {
            Name = reader.String("ClassName") ?? string.Empty,
            X = reader.Double("X"),
            Y = reader.Double("Y"),
            Width = reader.Double("Width"),
            Height = reader.Double("Height"),
            AnimationType = reader.Int("Type", 0),
            ScaleX = reader.Double("ScaleX", 1),
            ScaleY = reader.Double("ScaleY", 1),
        };
        if (reader.Has("Direction")) animation.Direction = reader.String("Direction") ?? "0|0";
        if (reader.Has("Acceleration")) animation.Acceleration = reader.String("Acceleration") ?? "0|0";
        if (reader.Has("Time")) animation.Time = reader.Double("Time");
        ReadCommon(animation, x, reader);
        return animation;
    }

    private ParticleElement ReadParticle(XElement x)
    {
        var reader = new AttributeReader(x);
        var particle = new ParticleElement
        {
            Name = reader.String("ClassName") ?? string.Empty,
            X = reader.Double("X"),
            Y = reader.Double("Y"),
            Width = reader.Double("Width"),
            Height = reader.Double("Height"),
            Frame = reader.Int("Frame"),
            ParticleType = reader.Int("Type", 1) == 2 ? 2 : 1,
        };
        ReadCommon(particle, x, reader);
        return particle;
    }

    // ---- comments ----

    private sealed class CommentData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    private static bool TryReadComment(XComment comment, out CommentElement element)
    {
        element = null!;
        string value = comment.Value.Trim();
        if (!value.StartsWith(CommentPrefix, StringComparison.Ordinal))
            return false;

        try
        {
            string json = value[CommentPrefix.Length..].Trim();
            var data = JsonSerializer.Deserialize<CommentData>(json);
            if (data == null)
                return false;
            element = new CommentElement
            {
                Name = data.Text,
                X = data.X,
                Y = data.Y,
                Width = data.Width,
                Height = data.Height,
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static XComment WriteComment(CommentElement comment)
    {
        var data = new CommentData
        {
            X = Math.Round(comment.X, 3),
            Y = Math.Round(comment.Y, 3),
            Width = Math.Round(comment.Width, 3),
            Height = Math.Round(comment.Height, 3),
            Text = comment.Name,
        };
        // JSON keeps quotes and line breaks safe; "--" is not allowed inside XML comments.
        string json = JsonSerializer.Serialize(data);
        while (json.Contains("--", StringComparison.Ordinal))
            json = json.Replace("--", "-\\u002D");
        return new XComment(" " + CommentPrefix + " " + json + " ");
    }
}
