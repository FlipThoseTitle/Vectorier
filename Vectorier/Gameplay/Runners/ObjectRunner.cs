// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;
using System.Xml;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Transformation;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Runners
{
    public class ObjectRunner
    {
        public string Name;

        public Vector2 LocalPosition;

        public Vector2 WorldPosition
        {
            get
            {
                if (Parent == null)
                {
                    return LocalPosition;
                }
                return Parent.WorldPosition + LocalPosition;
            }
        }

        public Elements Element;

        public ObjectRunner Parent;

        public List<ObjectRunner> Childs = new List<ObjectRunner>();

        public float Factor;

        public VisualContainer VisualContainer;

        protected Dictionary<string, TransformSystem> TransformationDictionary = new Dictionary<string, TransformSystem>();

        protected List<TransformSystem> TransformationData = new List<TransformSystem>();

        public ObjectRunner(ObjectRunner parent)
        {
            Parent = parent;
        }

        public void Parse(XmlNode node, XmlNode data, Dictionary<string, string> choices)
        {
            Name = node.Attributes["Name"].ParseString("");
            LocalPosition = new Vector2(node.Attributes["X"].ParseFloat(), node.Attributes["Y"].ParseFloat());
            Factor = node.Attributes["Factor"].ParseFloat();

            if (Parent != null)
            {
                LocalPosition += Parent.LocalPosition;
            }

            var localTransform = Elements.GetTransformationNode(node);
            ParseTransformationData(localTransform);

            CreateElement(node["Content"], choices);

            if (data != null)
            {
                var externalTransform = Elements.GetTransformationNode(data);
                ParseTransformationData(externalTransform);

                CreateElement(data["Content"], choices);
            }
        }

        public void CreateElement(XmlNode node, Dictionary<string, string> choices)
        {
            var element = new Elements(this);
            element.Parse(node, choices);
            if (Element == null)
            {
                Element = element;
            }
            else
            {
                Element.CopyFrom(element);
            }
        }

        public void CreateChild(XmlNode mainNode, Dictionary<string, string> choices)
        {
            if (mainNode["Properties"] != null && mainNode["Properties"]["Static"] != null && mainNode["Properties"]["Static"]["Selection"] != null)
            {
                var selectionNode = mainNode["Properties"]["Static"]["Selection"];
                if (choices[selectionNode.Attributes["Choice"].Value] != selectionNode.Attributes["Variant"].Value)
                {
                    return;
                }
            }

            string name = mainNode.Attributes["Name"].ParseString(string.Empty);

            XmlNode data = null;

            if (!string.IsNullOrEmpty(name))
            {
                // Safe load: an object placed inside itself (directly or deeper) would nest forever. Its definition
                // is not followed again; the inline content (if any) still loads.
                bool nested = false;
                for (var p = this; p != null; p = p.Parent)
                {
                    if (p.Name == name)
                    {
                        nested = true;
                        break;
                    }
                }
                if (nested)
                    Console.WriteLine($"Object \"{name}\" contains itself: the inner copy is skipped.");
                else
                    data = Sets.ObjectNode(name);
            }

            var objectRunner1 = new ObjectRunner(this);
            objectRunner1.Parse(mainNode, data, choices);
            objectRunner1.Init();
            Childs.Add(objectRunner1);
        }

        public void SetLayer(VisualContainer container)
        {
            LocalPosition = Vector2.Zero;

            VisualContainer = container;
            foreach (var child in Childs)
            {
                child.SetLayer(VisualContainer);
            }
            foreach (var runner in Element.Runners)
            {
                runner.Parent = VisualContainer;
                runner.Z += VisualContainer.Z;
            }
        }

        public void Init()
        {
            InitElements();
            foreach (var child in Childs)
            {
                BuildTranformationTable(child.TransformationData);
            }
            BuildTranformationTable(TransformationData);
        }

        private void InitElements()
        {
            foreach (var runner in Element.Runners)
            {
                runner.InitRunner(LocalPosition);
                BuildTranformationTable(runner.TransformationData);
            }
        }

        public virtual void ParseTransformationData(XmlNode node)
        {
            if (node == null || node["Dynamic"] == null)
            {
                return;
            }
            foreach (XmlNode node2 in node["Dynamic"])
            {
                if (node2.Name == "Transformation")
                {
                    var system = TransformSystem.Create(node2);
                    system.MainParent = this;
                    AddTransformationData(system);
                }
            }
        }

        public void BuildTranformationTable(List<TransformSystem> systems)
        {
            foreach (TransformSystem system in systems)
            {
                TransformationDictionary[system.Name] = system;
            }
        }

        public int RunTranformation(string name)
        {
            if (TransformationDictionary.ContainsKey(name))
            {
                TransformationDictionary[name].Reset();
                return TransformationDictionary[name].Run();
            }
            int num2 = 0;
            for (int j = 0; j < Childs.Count; j++)
            {
                int num3 = Childs[j].RunTranformation(name);
                if (num3 != 0)
                {
                    num2 = System.Math.Max(num3, num2);
                }
            }
            return num2;
        }

        public int GetTransformationFrame(string name)
        {
            if (TransformationDictionary.ContainsKey(name))
            {
                return TransformationDictionary[name].FramesCount;
            }
            int num2 = 0;
            for (int j = 0; j < Childs.Count; j++)
            {
                int transformationFrame = Childs[j].GetTransformationFrame(name);
                if (transformationFrame != 0)
                {
                    num2 = System.Math.Max(transformationFrame, num2);
                }
            }
            return num2;
        }

        protected void AddTransformationData(TransformSystem p_data)
        {
            if (TransformationData == null)
            {
                TransformationData = new List<TransformSystem>();
            }
            TransformationData.Add(p_data);
        }
    }
}
