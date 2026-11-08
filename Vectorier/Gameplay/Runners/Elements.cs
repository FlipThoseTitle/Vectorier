#nullable disable warnings
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Xml;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners.Animation;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Runners;

public class Elements
{
    public ObjectRunner Parent;

    public List<Runner> Runners = new List<Runner>();

    public List<VisualRunner> Visuals = new List<VisualRunner>();

    public List<PlatformRunner> Platforms = new List<PlatformRunner>();

    public List<TriggerRunner> Triggers = new List<TriggerRunner>();

    public List<AreaRunner> Areas = new List<AreaRunner>();

    public List<AnimationRunner> Animations = new List<AnimationRunner>();

    public List<ParticleRunner> Particles = new List<ParticleRunner>();

    public List<TrapezoidRunner> Trapezoids = new List<TrapezoidRunner>();

    public List<ItemRunner> Items = new List<ItemRunner>();

    public List<SpawnRunner> Spawns = new List<SpawnRunner>();

    public List<CameraRunner> Cameras = new List<CameraRunner>();

    public List<PrimitiveRunner> Primitives = new List<PrimitiveRunner>();

    public Elements(ObjectRunner parent)
    {
        Parent = parent;
    }

    public void Parse(XmlNode mainNode, Dictionary<string, string> choices)
    {
        if (mainNode == null)
        {
            return;
        }

        for (int i = 0; i < mainNode.ChildNodes.Count; i++)
        {
            XmlNode node = mainNode.ChildNodes[i];
            if (node["Properties"] != null && node["Properties"]["Static"] != null && node["Properties"]["Static"]["Selection"] != null)
            {
                var selection = node["Properties"]["Static"]["Selection"];
                if (choices[selection.Attributes["Choice"].Value] != selection.Attributes["Variant"].Value)
                {
                    continue;
                }
            }

            Runner runner = null;

            switch (node.Name)
            {
                case "Object":
                    Parent.CreateChild(node, choices);
                    continue;
                case "Image":
                    runner = CreateVisual(node);
                    if (runner != null)
                    {
                        Visuals.Add((VisualRunner)runner);
                    }
                    break;
                case "Trigger":
                    runner = CreateTrigger(node);
                    if (runner != null)
                        Triggers.Add((TriggerRunner)runner);
                    break;
                case "Area":
                    runner = CreateArea(node);
                    if (runner != null)
                        Areas.Add((AreaRunner)runner);
                    break;
                case "Spawn":
                    runner = CreateSpawn(node);
                    if (runner != null)
                        Spawns.Add((SpawnRunner)runner);
                    break;
                case "Camera":
                    runner = CreateCamera(node);
                    if (runner != null)
                        Cameras.Add((CameraRunner)runner);
                    break;
                case "Platform":
                    runner = CreatePlatform(node);
                    if (runner != null)
                        Platforms.Add((PlatformRunner)runner);
                    break;
                case "Trapezoid":
                    runner = CreateTrapezoid(node);
                    if (runner != null)
                        Trapezoids.Add((TrapezoidRunner)runner);
                    break;
                case "Item":
                    runner = CreateItem(node);
                    if (runner != null)
                        Items.Add((ItemRunner)runner);
                    break;
                case "Model":
                    runner = CreatePrimitive(node);
                    if (runner != null)
                        Primitives.Add((PrimitiveRunner)runner);
                    break;
                case "Animation":
                    runner = CreateAnimation(node);
                    if (runner != null)
                    {
                        Animations.Add((AnimationRunner)runner);
                    }
                    break;
                case "Particle":
                    runner = CreateParticle(node);
                    if (runner != null)
                    {
                        Particles.Add((ParticleRunner)runner);
                    }
                    break;
            }

            if (runner != null)
            {
                Runners.Add(runner);
            }
        }

        Init();
    }

    private TriggerRunner CreateTrigger(XmlNode Node)
    {
        var x = Node.Attributes["X"].ParseFloat();
        var y = Node.Attributes["Y"].ParseFloat();
        var width = Node.Attributes["Width"].ParseFloat();
        var height = Node.Attributes["Height"].ParseFloat();
        var trigger = new TriggerRunner(x, y, width, height, Node);
        trigger.SetXmlList(GetTransformationNode(Node));
        return trigger;
    }

    private AreaRunner CreateArea(XmlNode node)
    {
        AreaRunner area = null;
        var name = node.Attributes["Name"].ParseString();
        string type = node.Attributes["Type"].ParseString();
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        var width = node.Attributes["Width"].ParseFloat();
        var height = node.Attributes["Height"].ParseFloat();
        switch (type)
        {
            case "Trick":
                string itemName = node.Attributes["ItemName"].ParseString();
                int score = node.Attributes["Score"].ParseInt();
                area = new TrickAreaRunner(x, y, width, height, type, name, itemName, score);
                break;
            case "Catch":
                float distance = node.Attributes["Distance"].ParseFloat(200);
                area = new ArrestAreaRunner(x, y, width, height, type, name, distance);
                break;
            case "Help":
                string key = node.Attributes["Key"].ParseString();
                string description = node.Attributes["Description"].ParseString();
                area = new TutorialAreaRunner(x, y, width, height, type, name, key, description);
                break;
            default:
                area = new AreaRunner(AreaRunner.AreaType.None, x, y, width, height, type, name);
                break;
        }
        area.SetXmlList(GetTransformationNode(node));
        return area;
    }

    private VisualRunner CreateVisual(XmlNode node)
    {
        var name = node.Attributes["ClassName"].ParseString();
        int type = node.Attributes["Type"].ParseInt();
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        var width = node.Attributes["Width"].ParseFloat(float.NaN);
        var height = node.Attributes["Height"].ParseFloat(float.NaN);
        if (node.Attributes["TrMatrix"] != null)
        {
            width = node.Attributes["NativeX"].ParseFloat(float.NaN);
            height = node.Attributes["NativeY"].ParseFloat(float.NaN);
        }
        Color color = Color.White;
        if (node.Attributes["Color"] != null)
        {
            color = ColorUtils.FromHex(node.Attributes["Color"].Value);
        }
        if (node["Properties"] != null && node["Properties"]["Static"] != null && node["Properties"]["Static"]["StartColor"] != null)
        {
            color = ColorUtils.FromHex(node["Properties"]["Static"]["StartColor"].Attributes["Color"].Value);
        }
        int depth = node.Attributes["Depth"].ParseInt(-1);
        Matrix? matrix = null;

        if (node["Properties"] != null && node["Properties"]["Static"] != null && node["Properties"]["Static"]["Matrix"] != null)
        {
            XmlNode matrixNode = node["Properties"]["Static"]["Matrix"];
            var a = matrixNode.Attributes["A"].ParseFloat();
            var b = matrixNode.Attributes["B"].ParseFloat();
            var c = matrixNode.Attributes["C"].ParseFloat();
            var d = matrixNode.Attributes["D"].ParseFloat();
            var tX = matrixNode.Attributes["Tx"].ParseFloat();
            var tY = matrixNode.Attributes["Ty"].ParseFloat();

            matrix = new Matrix()
            {
                A = a,
                B = b,
                C = c,
                D = d,
                Tx = tX,
                Ty = tY
            };
        }
        var visual = new VisualRunner(name, x, y, width, height, type, depth, matrix, color);
        visual.SetXmlList(GetTransformationNode(node));
        return visual;
    }

    private PlatformRunner CreatePlatform(XmlNode node)
    {
        var name = node.Attributes["Name"].ParseString();
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        var width = node.Attributes["Width"].ParseFloat();
        var height = node.Attributes["Height"].ParseFloat();
        var sticky = node.Attributes["Sticky"].ParseBool(true);
        var platform = new PlatformRunner(name, x, y, width, height, sticky);
        platform.SetXmlList(GetTransformationNode(node));
        return platform;
    }

    private TrapezoidRunner CreateTrapezoid(XmlNode node)
    {
        var name = node.Attributes["Name"].ParseString();
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        int type = node.Attributes["Type"].ParseInt(1);
        var width = node.Attributes["Width"].ParseFloat();
        var height = node.Attributes["Height"].ParseFloat();
        var height1 = node.Attributes["Height1"].ParseFloat();
        var sticky = node.Attributes["Sticky"].ParseBool(true);
        var trapezoid = new TrapezoidRunner(name, type, x, y, width, height, height1, sticky);
        trapezoid.SetXmlList(GetTransformationNode(node));
        return trapezoid;
    }

    private ItemRunner CreateItem(XmlNode node)
    {
        ItemRunner item = null;
        int type = node.Attributes["Type"].ParseInt();
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        if (type == 0)
        {
            int score = node.Attributes["Score"].ParseInt();
            item = new ItemScoreRunner(type, "ScoreItem", score, x, y);
        }
        else
        {
            int groupId = node.Attributes["GroupId"].ParseInt();
            int score = node.Attributes["Score"].ParseInt();
            item = new CoinRunner(type, "CoinItem", groupId, score, x, y);
        }
        item.SetXmlList(GetTransformationNode(node));
        return item;
    }

    private PrimitiveRunner CreatePrimitive(XmlNode node)
    {
        PrimitiveRunner primitive = null;
        var name = node.Attributes["ClassName"].ParseString();
        int type = node.Attributes["Type"].ParseInt();
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        var impulse = node.Attributes["Impuls"].ParseFloat(30);
        List<string> sounds = node.Attributes["Sounds"].ParseString(string.Empty).Split('|').ToList();
        Vector3 deltaPosition = new Vector3(x, y, 0);
        if (type == 0)
        {
            primitive = new PrimitiveRunner(0, name, Color.Black, deltaPosition, impulse, sounds);
        }
        else
        {
            primitive = new PrimitiveAnimatedRunner(type, name, Color.Black, deltaPosition, impulse, sounds);
            primitive.Type = ModelType.PrimitiveAnimated;
        }
        primitive.SetXmlList(GetTransformationNode(node));
        return primitive;
    }

    private ParticleRunner CreateParticle(XmlNode node)
    {
        var name = node.Attributes["ClassName"].ParseString();
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        var width = node.Attributes["Width"].ParseFloat();
        var height = node.Attributes["Height"].ParseFloat();
        var particle = new ParticleRunner(x, y, width, height, name);
        particle.SetXmlList(GetTransformationNode(node));
        return particle;
    }

    private AnimationRunner CreateAnimation(XmlNode node)
    {
        AnimationRunner animation = null;
        var name = node.Attributes["ClassName"].ParseString();
        if (name == "p_dust2")
        {
            return null;
        }
        int type = node.Attributes["Type"].ParseInt();
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        var width = node.Attributes["Width"].ParseFloat();
        var height = node.Attributes["Height"].ParseFloat();
        var scaleX = node.Attributes["ScaleX"].ParseFloat(1);
        var scaleY = node.Attributes["ScaleY"].ParseFloat(1);
        if (type == 1)
        {
            Vector2 direction = Vector2.Zero;
            Vector2 acceleration = Vector2.Zero;
            int life = node.Attributes["Time"].ParseInt();
            if (node.Attributes["Direction"] != null)
            {
                string[] value = node.Attributes["Direction"].Value.Split('|');
                direction = new Vector2(float.Parse(value[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(value[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            if (node.Attributes["Acceleration"] != null)
            {
                string[] value = node.Attributes["Acceleration"].Value.Split('|');
                acceleration = new Vector2(float.Parse(value[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(value[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            animation = new AnimationVectorRunner(x, y, width, height, name, 1, scaleX, scaleY, direction, acceleration, life * 60);
        }
        else
        {
            animation = new AnimationRunner(x, y, width, height, name, type, scaleX, scaleY, false, 2);
        }
        animation.SetXmlList(GetTransformationNode(node));
        return animation;
    }

    private CameraRunner CreateCamera(XmlNode node)
    {
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        CameraRunner camera = new CameraRunner(x, y, "", "");
        camera.SetXmlList(GetTransformationNode(node));
        return camera;
    }

    private SpawnRunner CreateSpawn(XmlNode node)
    {
        var x = node.Attributes["X"].ParseFloat();
        var y = node.Attributes["Y"].ParseFloat();
        var animation = node.Attributes["Animation"].ParseString();
        var name = node.Attributes["Name"].ParseString();
        SpawnRunner spawn = new SpawnRunner(x, y, name, animation);
        spawn.SetXmlList(GetTransformationNode(node));
        return spawn;
    }

    public void Init()
    {
        foreach (var runner in Runners)
        {
            runner.ParentElements = this;
        }
    }

    public void CopyFrom(Elements element)
    {
        Visuals.AddRange(element.Visuals);
        Platforms.AddRange(element.Platforms);
        Triggers.AddRange(element.Triggers);
        Areas.AddRange(element.Areas);
        Animations.AddRange(element.Animations);
        Particles.AddRange(element.Particles);
        Trapezoids.AddRange(element.Trapezoids);
        Items.AddRange(element.Items);
        Cameras.AddRange(element.Cameras);
        Spawns.AddRange(element.Spawns);
        Primitives.AddRange(element.Primitives);
        Runners.AddRange(element.Runners);
        element.Reset();
        Init();
    }

    private void Reset()
    {
        Visuals.Clear();
        Platforms.Clear();
        Triggers.Clear();
        Areas.Clear();
        Animations.Clear();
        Particles.Clear();
        Trapezoids.Clear();
        Items.Clear();
        Cameras.Clear();
        Spawns.Clear();
        Primitives.Clear();
        Runners.Clear();
    }

    public static XmlNode GetTransformationNode(XmlNode node)
    {
        if (node["Properties"] != null && node["Properties"]["Dynamic"] != null)
            return node["Properties"];
        return null;
    }
}
