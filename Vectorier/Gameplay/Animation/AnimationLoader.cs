// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Animation
{
    public class AnimationLoader
    {
        public string _xmlPath;

        public static AnimationLoader Current
        {
            get;
        }

        public AnimationInfo Info
        {
            get;
        }

        static AnimationLoader()
        {
            Current = new AnimationLoader();
        }

        public void Init()
        {
            _xmlPath = VectorPaths.Animations;
            ParseAnimations();
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

        public void ParseAnimations()
        {
            Dictionary<string, XmlNode> dictionary = new Dictionary<string, XmlNode>();
            XmlNode xmlNode = XmlUtils.OpenXMLDocument(_xmlPath, "moves.xml")["root"];
            ParseConfigs(xmlNode["Config"]);
            foreach (XmlNode childNode in xmlNode["EventGroups"].ChildNodes)
            {
                dictionary[childNode.Attributes["Name"].Value] = childNode;
            }
            ParseGroups(xmlNode["ReactionGroups"]);
            XmlNode xmlNode3 = xmlNode["Moves"];
            foreach (XmlNode childNode2 in xmlNode3.ChildNodes)
            {
                AnimationInfo animationInfo;
                animationInfo = childNode2.Attributes["Trick"] != null && childNode2.Attributes["Trick"].ParseInt() != 0 ? new AnimationTrickInfo(childNode2) : new AnimationInfo(childNode2);
                foreach (XmlNode childNode3 in childNode2.ChildNodes)
                {
                    if (childNode3.Name != "Interval")
                    {
                        continue;
                    }
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
                Animations.Animation[animationInfo.Name] = animationInfo;
            }
            AnimationBinaryParser.ClearCachedBinary();
        }

        public static void ParseGroups(XmlNode node)
        {
            foreach (XmlNode childNode in node.ChildNodes)
            {
                List<AnimationReaction> list = new List<AnimationReaction>();
                foreach (XmlNode childNode2 in childNode.ChildNodes)
                {
                    list.Add(new AnimationReaction(childNode2));
                }
                AnimationGroup animationGroup = new AnimationGroup();
                animationGroup.Reactions = list;
                animationGroup.Name = childNode.Attributes["Name"].Value;
                AnimationGroup.Add(animationGroup);
            }
        }

        public static AnimationReaction ParseReaction(string[] pArray)
        {
            if (pArray.Length < 1 && int.Parse(pArray[1], CultureInfo.InvariantCulture) == 0)
            {
                return null;
            }
            string name = pArray[0];
            int frame = !string.IsNullOrEmpty(pArray[1]) ? int.Parse(pArray[1], CultureInfo.InvariantCulture) : 0;
            AnimationReaction animationReaction = new AnimationReaction(name, frame);
            if (pArray.Length == 2)
            {
                animationReaction.Reverse = false;
            }
            else
            {
                animationReaction.Reverse = int.Parse(pArray[2], CultureInfo.InvariantCulture) > 0;
            }
            return animationReaction;
        }

        private void ParseConfigs(XmlNode xmlNode)
        {
            XmlNode cameraNode = xmlNode["Camera"];
            LocationCamera.MinZoom = cameraNode.Attributes["MinZoom"].ParseFloat();
            LocationCamera.MaxZoom = cameraNode.Attributes["MaxZoom"].ParseFloat();
            LocationCamera.CurrentZoom = cameraNode.Attributes["CurrZoom"].ParseFloat();

            XmlNode taserNode = xmlNode["Taser"];
            ControllerCatching.DistanceFactor = taserNode.Attributes["Distance"].ParseFloat();
            ControllerCatching.Timeout = taserNode.Attributes["Time"].ParseFloat();
            ControllerCatching.HeightFactor = taserNode.Attributes["HeightFactor"].ParseFloat();
        }
    }
}
