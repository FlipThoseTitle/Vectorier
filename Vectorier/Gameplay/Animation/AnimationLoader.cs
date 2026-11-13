#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Diagnostics;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Animation;

public class AnimationLoader
{
    public string _xmlPath;

    public static AnimationLoader Current { get; }

    public AnimationInfo Info { get; }

    static AnimationLoader()
    {
        Current = new AnimationLoader();
    }

    public void Init()
    {
        _xmlPath = VectorPaths.Animations;
        ParseAnimations();
    }

    /// <summary>The editor's addition: the parsed moves are let go (the editor closed); the next game parses them again.</summary>
    public void Unload()
    {
        AnimationBinaryParser.ClearCachedBinary();
        AnimationGroup.ClearGroups();
        Animations.Animation.Clear();
        AnimationReaction.Reactions.Clear();
        AnimationTrickInfo.TricksLoaded.Clear();
        AnimationReaction.MovesVersion++;
    }

    public void ReloadAnimations()
    {
        AnimationBinaryParser.ClearCachedBinary();
        AnimationGroup.ClearGroups();
        Animations.Animation.Clear();
        AnimationReaction.Reactions.Clear();
        AnimationTrickInfo.TricksLoaded.Clear();
        ParseAnimations();
    }

    /// <summary>
    /// Reload Moves (debug panel): moves.xml and every move's .bin read again from the disk, so edited moves and
    /// new .bin files are used without restarting the game. When moves.xml cannot be read (a typo while
    /// editing), the moves loaded before are kept and the error is returned.
    /// </summary>
    public bool ReloadMoves(out string? error)
    {
        var animations = new Dictionary<string, AnimationInfo>(Animations.Animation);
        var groups = new Dictionary<string, AnimationGroup>(AnimationGroup.Groups);
        var reactions = new List<AnimationReaction>(AnimationReaction.Reactions);
        var tricks = new List<AnimationTrickInfo>(AnimationTrickInfo.TricksLoaded);
        try
        {
            // Read on its own first: a broken file reports its line and column instead of failing half-way.
            // Same reading as the game's (the text, so the file's encoding declaration is not used).
            XmlUtils.ReadXmlDocument(_xmlPath, "moves.xml");
        }
        catch (Exception ex)
        {
            error = GameLog.Error(ex, "Reloading the moves").Summary;
            return false;
        }
        try
        {
            ReloadAnimations();
            PathCase.Clear(); // a .bin added since the start is found whatever its casing
            Parallel.ForEach(Animations.ToList(), animation => animation.LoadBin());
            AnimationReaction.MovesVersion++;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            Restore(Animations.Animation, animations);
            Restore(AnimationGroup.Groups, groups);
            AnimationReaction.Reactions.Clear();
            AnimationReaction.Reactions.AddRange(reactions);
            AnimationTrickInfo.TricksLoaded.Clear();
            AnimationTrickInfo.TricksLoaded.AddRange(tricks);
            error = GameLog.Error(ex, "Reloading the moves").Summary;
            return false;
        }
    }

    private static void Restore<TValue>(Dictionary<string, TValue> target, Dictionary<string, TValue> saved)
    {
        target.Clear();
        foreach (var (key, value) in saved)
            target[key] = value;
    }

    public void ParseAnimations()
    {
        // Every element is noted while it is read (XmlSource.Note): an error in it names the move, interval,
        // group or reaction it is in, with its line in moves.xml.
        Dictionary<string, XmlNode> dictionary = new Dictionary<string, XmlNode>();
        XmlDocument document = XmlUtils.ReadXmlDocument(_xmlPath, "moves.xml");
        XmlNode xmlNode = XmlSource.RequiredChild(document, "root");
        ParseConfigs(XmlSource.RequiredChild(xmlNode, "Config"));
        foreach (XmlNode childNode in XmlSource.RequiredChild(xmlNode, "EventGroups").ChildNodes)
        {
            dictionary[XmlSource.Required(childNode, "Name")] = childNode;
        }
        ParseGroups(XmlSource.RequiredChild(xmlNode, "ReactionGroups"));
        XmlNode xmlNode3 = XmlSource.RequiredChild(xmlNode, "Moves");
        foreach (XmlNode childNode2 in xmlNode3.ChildNodes)
        {
            try
            {
                AnimationInfo animationInfo;
                animationInfo = childNode2.Attributes["Trick"] != null && childNode2.Attributes["Trick"].ParseInt() != 0 ? new AnimationTrickInfo(childNode2) : new AnimationInfo(childNode2);
                foreach (XmlNode childNode3 in childNode2.ChildNodes)
                {
                    if (childNode3.Name != "Interval")
                    {
                        continue;
                    }
                    try
                    {
                        AnimationInterval animationInterval = null;
                        if (childNode3.Attributes["Groups"] == null)
                        {
                            animationInterval = new AnimationInterval(childNode3);
                        }
                        else
                        {
                            List<XmlNode> list = new List<XmlNode>();
                            string[] array = childNode3.Attributes["Groups"].Value.Split('|');
                            foreach (string key in array)
                            {
                                if (!dictionary.ContainsKey(key))
                                {
                                    continue;
                                }
                                list.Add(dictionary[key]);
                            }
                            animationInterval = new AnimationInterval(childNode3, list);
                        }
                        animationInfo.Intervals.Add(animationInterval);
                    }
                    catch (Exception ex) when (XmlSource.Note(ex, childNode3)) { throw; }
                }
                Animations.Animation[animationInfo.Name] = animationInfo;
            }
            catch (Exception ex) when (XmlSource.Note(ex, childNode2)) { throw; }
        }
        AnimationBinaryParser.ClearCachedBinary();
    }

    public static void ParseGroups(XmlNode node)
    {
        foreach (XmlNode childNode in node.ChildNodes)
        {
            try
            {
                List<AnimationReaction> list = new List<AnimationReaction>();
                foreach (XmlNode childNode2 in childNode.ChildNodes)
                {
                    try
                    {
                        list.Add(new AnimationReaction(childNode2));
                    }
                    catch (Exception ex) when (XmlSource.Note(ex, childNode2)) { throw; }
                }
                AnimationGroup animationGroup = new AnimationGroup();
                animationGroup.Reactions = list;
                animationGroup.Name = XmlSource.Required(childNode, "Name");
                AnimationGroup.Add(animationGroup);
            }
            catch (Exception ex) when (XmlSource.Note(ex, childNode)) { throw; }
        }
    }

    public static AnimationReaction ParseReaction(string[] pArray)
    {
        if (pArray.Length < 1 && int.Parse(pArray[1], System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            return null;
        }
        string name = pArray[0];
        int frame = !string.IsNullOrEmpty(pArray[1]) ? int.Parse(pArray[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
        AnimationReaction animationReaction = new AnimationReaction(name, frame);
        if (pArray.Length == 2)
        {
            animationReaction.Reverse = false;
        }
        else
        {
            animationReaction.Reverse = int.Parse(pArray[2], System.Globalization.CultureInfo.InvariantCulture) > 0;
        }
        return animationReaction;
    }

    private void ParseConfigs(XmlNode xmlNode)
    {
        XmlNode cameraNode = xmlNode["Camera"];
        // Attributes left out keep the game's defaults.
        float Read(string name, float fallback) => cameraNode?.Attributes?[name] is { } a ? a.ParseFloat() : fallback;
        LocationCamera.Config = (Read("MinZoom", 0.1f), Read("MaxZoom", 1.3f), Read("CurrZoom", 0.5f),
            System.Math.Max(0.001f, Read("MaxSpeed", 50f)), System.Math.Max(0.001f, Read("Fluency", 2f)));
        LocationCamera.MinZoom = LocationCamera.Config.MinZoom;
        LocationCamera.MaxZoom = LocationCamera.Config.MaxZoom;
        LocationCamera.CurrentZoom = LocationCamera.Config.CurrZoom;
        LocationCamera.MaxSpeed = LocationCamera.Config.MaxSpeed;
        LocationCamera.FluencyCurrent = LocationCamera.Config.Fluency;

        XmlNode taserNode = XmlSource.RequiredChild(xmlNode, "Taser");
        ControllerCatching.DistanceFactor = taserNode.Attributes["Distance"].ParseFloat();
        ControllerCatching.Timeout = taserNode.Attributes["Time"].ParseFloat();
        ControllerCatching.HeightFactor = taserNode.Attributes["HeightFactor"].ParseFloat();
    }
}
