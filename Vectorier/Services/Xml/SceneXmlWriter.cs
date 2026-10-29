using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Media;
using Vectorier.Models.Scene;

namespace Vectorier.Services.Xml;

/// <summary>
/// Serializes the editor's scene model back into Vector XML. Only game data is written: editor-only
/// information (resolved reference content, selection state...) never reaches the file.
/// </summary>
public sealed class SceneXmlWriter
{
    /// <summary>Returns the native texture size for a class name (used for images written with a matrix).</summary>
    public Func<string, (int Width, int Height)?>? TextureSizeProvider { get; set; }

    public List<string> Errors { get; } = new();

    // ================= LEVEL ================= //

    public XDocument WriteLevel(SceneDocument scene)
    {
        var settings = scene.Settings;
        var root = new XElement("Root");

        var sets = new XElement("Sets");
        foreach (var file in settings.CitySets.Where(f => !string.IsNullOrWhiteSpace(f)))
            sets.Add(new XElement("City", new XAttribute("FileName", file)));
        foreach (var file in settings.GroundSets.Where(f => !string.IsNullOrWhiteSpace(f)))
            sets.Add(new XElement("Ground", new XAttribute("FileName", file)));
        foreach (var file in settings.LibrarySets.Where(f => !string.IsNullOrWhiteSpace(f)))
            sets.Add(new XElement("Library", new XAttribute("FileName", file)));
        if (sets.HasElements)
            root.Add(sets);

        if (!string.IsNullOrEmpty(settings.MusicName))
        {
            root.Add(new XElement("Music",
                new XAttribute("Name", settings.MusicName),
                new XAttribute("Volume", Num.Format(settings.MusicVolume))));
        }

        if (settings.CommonModels.Count > 0)
            root.Add(WriteModels(settings.CommonModels, "CommonMode"));
        if (settings.HunterModels.Count > 0)
            root.Add(WriteModels(settings.HunterModels, "HunterMode"));

        // The game always reads <Coins>, so it is always written.
        root.Add(new XElement("Coins", new XAttribute("Value", settings.CoinValue.ToString(CultureInfo.InvariantCulture))));
        root.Add(new XElement("Objects", new XAttribute("Name", "Money")));

        foreach (var extra in settings.ExtraRootElements)
            root.Add(new XElement(extra));

        root.Add(WriteTrack(scene));
        return new XDocument(root);
    }

    private static XElement WriteModels(IEnumerable<LevelModelDefinition> models, string variant)
    {
        var element = new XElement("Models",
            new XAttribute("Choice", "AITriggers"),
            new XAttribute("Variant", variant));
        foreach (var model in models)
        {
            var m = new XElement("Model");
            foreach (var (key, value) in model.Attributes)
                m.SetAttributeValue(key, value);
            element.Add(m);
        }
        return element;
    }

    private XElement WriteTrack(SceneDocument scene)
    {
        var track = new XElement("Track");

        // Group top-level elements by the factor of their layer.
        var groups = new Dictionary<double, (string Text, List<SceneElement> Elements)>();
        foreach (var factorText in scene.Settings.SourceFactors)
        {
            double factor = Num.Parse(factorText, 1);
            if (!groups.ContainsKey(factor))
                groups[factor] = (factorText, new List<SceneElement>());
        }

        foreach (var element in scene.Roots)
        {
            string layer = element.LayerEditable ? element.Layer : Layers.Default;
            double factor = Layers.GetFactor(layer);
            if (!groups.TryGetValue(factor, out var group))
            {
                group = (Layers.GetFactorText(layer), new List<SceneElement>());
                groups[factor] = group;
            }
            group.Elements.Add(element);
        }

        foreach (var (factor, group) in groups.OrderBy(g => g.Key))
        {
            var layerObject = new XElement("Object", new XAttribute("Factor", group.Text));
            if (group.Elements.Count > 0)
            {
                var content = new XElement("Content");
                WriteContent(content, group.Elements);
                layerObject.Add(content);
            }
            track.Add(layerObject);
        }

        return track;
    }

    // ================= OBJECTS FILES ================= //

    /// <summary>Writes an object definition (prefab) for an objects XML file.</summary>
    public XElement WriteDefinition(string name, IEnumerable<SceneElement> content)
    {
        var obj = new XElement("Object", new XAttribute("Name", name));
        var contentElement = new XElement("Content");
        WriteContent(contentElement, content.ToList());
        obj.Add(contentElement);
        return obj;
    }

    // ================= CONTENT ================= //

    /// <summary>Writes siblings in hierarchy order, stable-sorted by Order in Layer.</summary>
    public void WriteContent(XElement content, IList<SceneElement> elements)
    {
        var ordered = elements
            .Select((e, index) => (Element: e, Index: index))
            .OrderBy(t => t.Element.Order)
            .ThenBy(t => t.Index)
            .Select(t => t.Element);

        foreach (var element in ordered)
        {
            var node = WriteElement(element);
            if (node != null)
                content.Add(node);
        }
    }

    public XNode? WriteElement(SceneElement element)
    {
        switch (element)
        {
            case CommentElement comment:
                return SceneXmlReader.WriteComment(comment);
            case UnknownElement unknown:
                return new XElement(unknown.Xml);
        }

        XElement x = element switch
        {
            ObjectElement obj => WriteObject(obj),
            ImageElement image => WriteImage(image),
            TriggerElement trigger => WriteTrigger(trigger),
            AreaElement area => WriteArea(area),
            PlatformElement platform => WriteBox("Platform", platform, includeName: !string.IsNullOrEmpty(platform.Name)),
            TrapezoidElement trapezoid => WriteTrapezoid(trapezoid),
            CameraElement camera => WritePoint("Camera", camera),
            SpawnElement spawn => WriteSpawn(spawn),
            ModelElement model => WriteModel(model),
            ItemElement item => WriteItem(item),
            AnimationElement animation => WriteAnimation(animation),
            ParticleElement particle => WriteParticle(particle),
            _ => throw new NotSupportedException(element.GetType().Name),
        };
        return x;
    }

    // ---- shared parts ----

    private static void Attr(XElement x, string name, double value) => x.SetAttributeValue(name, Num.Format(value));
    private static void Attr(XElement x, string name, int value) => x.SetAttributeValue(name, value.ToString(CultureInfo.InvariantCulture));
    private static void Attr(XElement x, string name, string? value)
    {
        if (value != null)
            x.SetAttributeValue(name, value);
    }

    /// <summary>Adds preserved attributes, Properties (Static, Dynamic) and preserved children.</summary>
    private void Finish(XElement x, SceneElement element, IEnumerable<XElement>? staticItems = null)
    {
        foreach (var (key, value) in element.ExtraAttributes)
        {
            if (x.Attribute(key) == null)
                x.SetAttributeValue(key, value);
        }

        var properties = new XElement("Properties");
        var staticElement = new XElement("Static");

        if (staticItems != null)
        {
            foreach (var item in staticItems)
                staticElement.Add(item);
        }

        if (element.Selection != ModeSelection.All)
        {
            staticElement.Add(new XElement("Selection",
                new XAttribute("Choice", "AITriggers"),
                new XAttribute("Variant", element.Selection.ToString())));
        }

        foreach (var extra in element.ExtraStatic)
            staticElement.Add(new XElement(extra));

        if (staticElement.HasElements)
            properties.Add(staticElement);

        if (element.Dynamics.Count > 0)
        {
            var dynamic = new XElement("Dynamic");
            foreach (var transformation in element.Dynamics)
                dynamic.Add(WriteTransformation(transformation));
            properties.Add(dynamic);
        }

        foreach (var extra in element.ExtraProperties)
            properties.Add(new XElement(extra));

        // Properties come before Content, as in the game's files.
        if (properties.HasElements)
        {
            var content = x.Element("Content");
            if (content != null)
                content.AddBeforeSelf(properties);
            else
                x.Add(properties);
        }

        foreach (var extra in element.ExtraChildren)
            x.Add(new XElement(extra));
    }

    public static XElement WriteTransformation(DynamicTransformation t)
    {
        var x = new XElement("Transformation", new XAttribute("Name", t.Name ?? "Transform"));

        if (t.Moves.Count > 0)
        {
            var move = new XElement("Move");
            for (int i = 0; i < t.Moves.Count; i++)
            {
                var m = t.Moves[i];
                move.Add(new XElement("MoveInterval",
                    new XAttribute("Number", i + 1),
                    new XAttribute("FramesToMove", m.Frames),
                    new XAttribute("Delay", Num.Format(m.Delay)),
                    new XElement("Point", new XAttribute("Name", "Start"), new XAttribute("X", "0"), new XAttribute("Y", "0")),
                    new XElement("Point", new XAttribute("Name", "Support"), new XAttribute("Number", "1"),
                        new XAttribute("X", Num.Format(m.SupportX)), new XAttribute("Y", Num.Format(m.SupportY))),
                    new XElement("Point", new XAttribute("Name", "Finish"),
                        new XAttribute("X", Num.Format(m.FinishX)), new XAttribute("Y", Num.Format(m.FinishY)))));
            }
            x.Add(move);
        }

        foreach (var s in t.Sizes)
        {
            x.Add(new XElement("Size",
                new XAttribute("Frames", s.Frames),
                new XAttribute("FinalWidth", Num.Format(s.FinalWidth)),
                new XAttribute("FinalHeight", Num.Format(s.FinalHeight))));
        }

        foreach (var r in t.Rotations)
        {
            x.Add(new XElement("Rotation",
                new XAttribute("Angle", Num.Format(r.Angle)),
                new XAttribute("Anchor", Num.Format(r.AnchorX) + "|" + Num.Format(r.AnchorY)),
                new XAttribute("Frames", r.Frames)));
        }

        foreach (var c in t.Colors)
        {
            x.Add(new XElement("Color",
                new XAttribute("ColorStart", XmlHelpers.ToRgba(c.Start)),
                new XAttribute("ColorFinish", XmlHelpers.ToRgba(c.Finish)),
                new XAttribute("Frames", c.Frames)));
        }

        return x;
    }

    // ---- element types ----

    private XElement WriteObject(ObjectElement obj)
    {
        var x = new XElement("Object");
        if (!string.IsNullOrEmpty(obj.Name))
            Attr(x, "Name", obj.Name);
        if (obj.PositionWasPresent || obj.X != 0 || obj.Y != 0)
        {
            Attr(x, "X", obj.X);
            Attr(x, "Y", obj.Y);
        }

        // Only inline children are written; content resolved from <Sets> stays in its own file.
        if (obj.Children.Count > 0)
        {
            var content = new XElement("Content");
            WriteContent(content, obj.Children);
            x.Add(content);
        }

        Finish(x, obj);
        return x;
    }

    private XElement WriteImage(ImageElement image)
    {
        var x = new XElement("Image");
        var staticItems = new List<XElement>();

        if (image.Color != Colors.White)
            staticItems.Add(new XElement("StartColor", new XAttribute("Color", XmlHelpers.ToRgba(image.Color))));

        if (image.IsAxisAligned && !image.HasMatrix)
        {
            Attr(x, "X", image.X);
            Attr(x, "Y", image.Y);
            Attr(x, "ClassName", image.ClassName);
            if (!image.AutoSize)
            {
                Attr(x, "Width", image.Width);
                Attr(x, "Height", image.Height);
            }
            if (image.NativeWidth > 0 && image.NativeHeight > 0)
            {
                Attr(x, "NativeX", image.NativeWidth);
                Attr(x, "NativeY", image.NativeHeight);
            }
        }
        else
        {
            var corners = image.GetLocalCorners();
            double minX = corners.Min(c => c.X), minY = corners.Min(c => c.Y);
            double maxX = corners.Max(c => c.X), maxY = corners.Max(c => c.Y);
            var (a, b, c, d) = image.GetBasis();

            int nativeW = image.NativeWidth, nativeH = image.NativeHeight;
            if ((nativeW <= 0 || nativeH <= 0) && TextureSizeProvider?.Invoke(image.ClassName) is { } size)
            {
                nativeW = size.Width;
                nativeH = size.Height;
            }
            if (nativeW <= 0 || nativeH <= 0)
            {
                nativeW = (int)Math.Round(image.Width);
                nativeH = (int)Math.Round(image.Height);
            }

            Attr(x, "X", minX);
            Attr(x, "Y", minY);
            Attr(x, "ClassName", image.ClassName);
            Attr(x, "Width", maxX - minX);
            Attr(x, "Height", maxY - minY);
            Attr(x, "NativeX", nativeW);
            Attr(x, "NativeY", nativeH);

            staticItems.Add(new XElement("Matrix",
                new XAttribute("A", Num.FormatMatrix(a)),
                new XAttribute("B", Num.FormatMatrix(b)),
                new XAttribute("C", Num.FormatMatrix(c)),
                new XAttribute("D", Num.FormatMatrix(d)),
                new XAttribute("Tx", Num.FormatMatrix(image.X - minX)),
                new XAttribute("Ty", Num.FormatMatrix(image.Y - minY))));
        }

        if (image.ImageType != ImageType.None)
            Attr(x, "Type", (int)image.ImageType);
        if (image.Depth == ImageDepth.Front)
            Attr(x, "Depth", 0);
        else if (image.Depth == ImageDepth.Back)
            Attr(x, "Depth", 1);

        Finish(x, image, staticItems);
        return x;
    }

    private XElement WriteTrigger(TriggerElement trigger)
    {
        var x = new XElement("Trigger");
        Attr(x, "Name", trigger.Name);
        Attr(x, "X", trigger.X);
        Attr(x, "Y", trigger.Y);
        Attr(x, "Width", trigger.Width);
        Attr(x, "Height", trigger.Height);

        var content = new XElement("Content");
        try
        {
            foreach (var node in XmlHelpers.ParseFragment(trigger.ContentXml))
                content.Add(node);
        }
        catch (XmlException ex)
        {
            Errors.Add($"Trigger '{trigger.Name}' has invalid content and was written without it: {ex.Message}");
        }
        x.Add(content);

        Finish(x, trigger);
        return x;
    }

    private XElement WriteArea(AreaElement area)
    {
        var x = new XElement("Area");
        Attr(x, "Name", area.Name);
        Attr(x, "X", area.X);
        Attr(x, "Y", area.Y);
        Attr(x, "Width", area.Width);
        Attr(x, "Height", area.Height);
        Attr(x, "Type", string.IsNullOrEmpty(area.AreaType) ? "Animation" : area.AreaType);

        switch (area.AreaType)
        {
            case "Catch":
                Attr(x, "Distance", area.Distance);
                break;
            case "Trick":
                Attr(x, "ItemName", area.ItemName);
                Attr(x, "Score", area.Score);
                break;
            case "Help":
                Attr(x, "Key", area.Key);
                Attr(x, "Description", area.Description ?? string.Empty);
                break;
        }

        Finish(x, area);
        return x;
    }

    private XElement WriteBox(string tag, SceneElement element, bool includeName)
    {
        var x = new XElement(tag);
        if (includeName)
            Attr(x, "Name", element.Name);
        bool integer = element.IntegerTransform;
        Attr(x, "X", integer ? Math.Round(element.X) : element.X);
        Attr(x, "Y", integer ? Math.Round(element.Y) : element.Y);
        Attr(x, "Width", integer ? Math.Round(element.Width) : element.Width);
        Attr(x, "Height", integer ? Math.Round(element.Height) : element.Height);
        Finish(x, element);
        return x;
    }

    private XElement WriteTrapezoid(TrapezoidElement trapezoid)
    {
        var x = new XElement("Trapezoid");
        if (!string.IsNullOrEmpty(trapezoid.Name))
            Attr(x, "Name", trapezoid.Name);

        double slope = Math.Round(trapezoid.SlopeHeight);
        double baseHeight = Math.Max(1, Math.Round(trapezoid.BaseHeight));
        double total = baseHeight + slope;

        Attr(x, "X", Math.Round(trapezoid.X));
        if (trapezoid.TrapezoidType == 2)
        {
            Attr(x, "Y", Math.Round(trapezoid.Y));
            Attr(x, "Width", Math.Round(trapezoid.Width));
            Attr(x, "Height", total);
            Attr(x, "Height1", baseHeight);
            Attr(x, "Type", 2);
        }
        else
        {
            Attr(x, "Y", Math.Round(trapezoid.Y + slope));
            Attr(x, "Width", Math.Round(trapezoid.Width));
            Attr(x, "Height", baseHeight);
            Attr(x, "Height1", total);
            Attr(x, "Type", 1);
        }

        Finish(x, trapezoid);
        return x;
    }

    private XElement WritePoint(string tag, SceneElement element)
    {
        var x = new XElement(tag);
        Attr(x, "X", element.X);
        Attr(x, "Y", element.Y);
        Finish(x, element);
        return x;
    }

    private XElement WriteSpawn(SpawnElement spawn)
    {
        var x = new XElement("Spawn");
        Attr(x, "X", spawn.X);
        Attr(x, "Y", spawn.Y);
        Attr(x, "Name", string.IsNullOrEmpty(spawn.Name) ? SpawnElement.DefaultName : spawn.Name);
        Attr(x, "Animation", string.IsNullOrWhiteSpace(spawn.Animation) ? SpawnElement.DefaultAnimation : spawn.Animation);
        Finish(x, spawn);
        return x;
    }

    private XElement WriteModel(ModelElement model)
    {
        var x = new XElement("Model");
        Attr(x, "X", model.X);
        Attr(x, "Y", model.Y);
        Attr(x, "Type", (int)model.ModelType);
        Attr(x, "ClassName", model.ClassName);
        Attr(x, "LifeTime", model.LifeTime);
        Finish(x, model);
        return x;
    }

    private XElement WriteItem(ItemElement item)
    {
        var x = new XElement("Item");
        Attr(x, "X", item.X);
        Attr(x, "Y", item.Y);
        Attr(x, "Score", item.Score);
        Attr(x, "Radius", item.Radius);
        Attr(x, "Type", (int)item.ItemType);
        if (item.ItemType == ItemType.Coin)
            Attr(x, "GroupId", item.GroupId);
        Finish(x, item);
        return x;
    }

    private XElement WriteAnimation(AnimationElement animation)
    {
        var x = new XElement("Animation");
        Attr(x, "X", animation.X);
        Attr(x, "Y", animation.Y);
        Attr(x, "Width", animation.Width);
        Attr(x, "Height", animation.Height);
        Attr(x, "ClassName", animation.ClassName);
        Attr(x, "Type", animation.AnimationType);
        Attr(x, "ScaleX", animation.ScaleX);
        Attr(x, "ScaleY", animation.ScaleY);
        if (animation.AnimationType == 1)
        {
            Attr(x, "Direction", animation.Direction);
            Attr(x, "Acceleration", animation.Acceleration);
            Attr(x, "Time", animation.Time);
        }
        Finish(x, animation);
        return x;
    }

    private XElement WriteParticle(ParticleElement particle)
    {
        var x = new XElement("Particle");
        Attr(x, "X", particle.X);
        Attr(x, "Y", particle.Y);
        Attr(x, "Width", particle.Width);
        Attr(x, "Height", particle.Height);
        Attr(x, "Frame", particle.Frame);
        Attr(x, "Type", particle.ParticleType);
        Attr(x, "ClassName", particle.ClassName);
        Finish(x, particle);
        return x;
    }
}
