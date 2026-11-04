// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml;
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Geometry;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Models
{
    public class ModelObject
    {
        private Model _Parent;

        private List<ModelRender> _Renders = new List<ModelRender>();

        private bool _isVisible;

        private bool _debugMode;

        private Color _Color;

        private List<ModelNode> _NodesAll = new List<ModelNode>();

        private Rectangle _Rectangle = new Rectangle();

        private List<int[]> _BothNodeList;

        private Capsule _botIconPosition;

        public Model Parent
        {
            get => _Parent;
            set
            {
                _Parent = value;
                _Renders[0].Add(_Parent);
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                foreach (var render in _Renders)
                {
                    foreach (var capsule in render.Capsules)
                    {
                        capsule.IsVisible = value;
                    }
                    foreach (var triangle in render.Triangles)
                    {
                        triangle.IsVisible = value;
                    }
                }
            }
        }

        public bool DebugMode
        {
            get => _debugMode;
            set => _debugMode = value;
        }

        public Color Color
        {
            get => _Color;
            set
            {
                _Color = value;
                foreach (var render in _Renders)
                {
                    render.Color = value;
                }
            }
        }

        public string Name
        {
            get;
            set;
        }

        public List<ModelNode> CenterOfMass
        {
            get;
        }

        public List<ModelNode> Nodes
        {
            get;
        }

        public List<ModelNode> MacroNodes
        {
            get;
        }

        public List<ModelNode> NodesAll => _NodesAll;

        public List<ModelNode> CollisibleNodes
        {
            get;
        }

        public List<ModelLine> Edges
        {
            get;
        }

        public List<ModelLine> Muscules
        {
            get;
        }

        public List<ModelLine> EdgesAll
        {
            get;
        }

        public List<ModelLine> EdgesPhysic
        {
            get;
        }

        public List<ModelLine> CollisibleEdges
        {
            get;
        }

        public List<ModelLine> Capsules
        {
            get;
        }

        private List<Triangle> Triangles
        {
            get;
        }

        public float DeltaBox
        {
            get;
            set;
        }

        public Rectangle Rectangle
        {
            get
            {
                _Rectangle.Origin.X = 0f - DeltaBox;
                _Rectangle.Origin.Y = 0f - DeltaBox;
                _Rectangle.Size.X = DeltaBox * 2f;
                _Rectangle.Size.Y = DeltaBox * 2f;
                return _Rectangle;
            }
        }

        public Rectangle BoundingBox
        {
            get;
            set;
        }

        public ModelNode PivotNode
        {
            get;
            private set;
        }

        public ModelNode DetectorHorizontalNode
        {
            get;
            private set;
        }

        public ModelNode DetectorVerticalNode
        {
            get;
            private set;
        }

        public DetectorLine DetectorVerticalLine
        {
            get;
            private set;
        }

        public DetectorLine DetectorHorizontalLine
        {
            get;
            private set;
        }

        public ModelNode CenterOfMassNode
        {
            get;
            private set;
        }

        public ModelNode ToeRight
        {
            get;
            private set;
        }

        public ModelNode ToeLeft
        {
            get;
            private set;
        }

        public CameraNode CameraNode
        {
            get;
            private set;
        }

        public bool IsAuxiliary
        {
            get;
            set;
        }

        public Vector3 Velocity
        {
            get
            {
                if (CenterOfMassNode == null)
                {
                    return new Vector3();
                }

                if (Parent is ModelHuman human && human.ControllerAnimations != null)
                {
                    var node = CenterOfMassNode;

                    Vector3 renderedDelta = node.Start - node.End;
                    return renderedDelta * human.ControllerAnimations.VelocityScale;
                }

                return CenterOfMassNode.Start - CenterOfMassNode.End;
            }
        }

        public List<int[]> BothNodeList => _BothNodeList;

        /// <summary>
        /// A model from files the editor has already read (models/0.xml + skins, runner.xml...), each a parsed
        /// &lt;Scene&gt; document, in the order the game would load them.
        /// </summary>
        public ModelObject(IEnumerable<KeyValuePair<string, XmlNode>> files) : this(new List<string>())
        {
            ParseDocuments(files);
            CreateBothNodeList();
        }

        public ModelObject(List<string> skins)
        {
            _botIconPosition = null;
            CenterOfMass = new List<ModelNode>();
            Nodes = new List<ModelNode>();
            MacroNodes = new List<ModelNode>();
            CollisibleNodes = new List<ModelNode>();
            Edges = new List<ModelLine>();
            Muscules = new List<ModelLine>();
            EdgesAll = new List<ModelLine>();
            EdgesPhysic = new List<ModelLine>();
            EdgesAll = new List<ModelLine>();
            CollisibleEdges = new List<ModelLine>();
            Capsules = new List<ModelLine>();
            Triangles = new List<Triangle>();
            DeltaBox = 200;
            IsAuxiliary = true;
            Parse(skins);
            CreateBothNodeList();
        }

        public void Parse(List<string> skins)
        {
            ParseAll(() =>
            {
                foreach (string s in skins)
                {
                    ParseFile(s);
                }
            });
        }

        private void ParseDocuments(IEnumerable<KeyValuePair<string, XmlNode>> files)
        {
            ParseAll(() =>
            {
                foreach (var file in files)
                {
                    ParseFile(file.Key, file.Value);
                }
            });
        }

        private void ParseAll(Action parseFiles)
        {
            _NodesAll.Clear();
            _Renders.Clear();
            CenterOfMass.Clear();
            Nodes.Clear();
            MacroNodes.Clear();
            CollisibleNodes.Clear();
            Edges.Clear();
            Muscules.Clear();
            EdgesAll.Clear();
            EdgesPhysic.Clear();
            EdgesAll.Clear();
            CollisibleEdges.Clear();
            Capsules.Clear();
            Triangles.Clear();
            parseFiles();
            CameraNode = null;
            PivotNode = GetNode();
            DetectorHorizontalNode = GetNode("DetectorH");
            DetectorVerticalNode = GetNode("DetectorV");
            if (DetectorHorizontalNode == null)
            {
                DetectorHorizontalLine = null;
            }
            else
            {
                DetectorHorizontalNode.IsDetector = false;
                DetectorHorizontalLine = new DetectorLine(DetectorHorizontalNode, DetectorLine.DetectorType.Horizontal);
            }
            if (DetectorVerticalNode == null)
            {
                DetectorVerticalLine = null;
            }
            else
            {
                DetectorVerticalNode.IsDetector = false;
                DetectorVerticalLine = new DetectorLine(DetectorVerticalNode, DetectorLine.DetectorType.Vertical);
            }
            CenterOfMassNode = GetNode("COM");
            ToeRight = GetNode("NToe_1");
            ToeLeft = GetNode("NToe_2");
            CameraNode = new CameraNode(GetNode("Camera"));
            UpdateBoundingBox();
        }

        public void UpdateBoundingBox()
        {
            RenderMacroNode();
            BoundingBox = new Rectangle(float.NaN, float.NaN, float.NaN, float.NaN);
            ModelNode modelNode;
            for (int i = 0; i < _NodesAll.Count; i++)
            {
                modelNode = _NodesAll[i];
                if (double.IsNaN(BoundingBox.Origin.X) || modelNode.Start.X < BoundingBox.Origin.X)
                {
                    BoundingBox.Origin.X = (float)modelNode.Start.X;
                }
                if (double.IsNaN(BoundingBox.Origin.Y) || modelNode.Start.Y < BoundingBox.Origin.Y)
                {
                    BoundingBox.Origin.Y = (float)modelNode.Start.Y;
                }
                if (double.IsNaN(BoundingBox.Size.X) || modelNode.Start.X > BoundingBox.MaxX)
                {
                    BoundingBox.Size.X = (float)modelNode.Start.X - BoundingBox.Origin.X;
                }
                if (double.IsNaN(BoundingBox.Size.Y) || modelNode.Start.Y > BoundingBox.MaxY)
                {
                    BoundingBox.Size.Y = (float)modelNode.Start.Y - BoundingBox.Origin.Y;
                }
            }
        }

        private void ParseFile(string file)
        {
            ParseFile(file, XmlUtils.OpenXMLDocument(VectorPaths.Models, file, XmlUtils.OpenXmlType.Normal, true, false));
        }

        private void ParseFile(string file, XmlNode xmlNode)
        {
            ModelRender modelRender = new ModelRender();
            modelRender.Name = "[Skins] " + file;
            ModelRender modelRender2 = modelRender;
            if (xmlNode == null)
            {
                return;
            }
            xmlNode = xmlNode["Scene"];
            ParseNodes(xmlNode["Nodes"], modelRender2);
            ParseEdges(xmlNode["Edges"], modelRender2);
            ParseCapsules(xmlNode["Figures"], modelRender2);
            _Renders.Add(modelRender2);
        }

        public void ParseNodes(XmlNode nodes, ModelRender render)
        {
            if (nodes == null)
            {
                return;
            }
            foreach (XmlNode childNode in nodes.ChildNodes)
            {
                ModelNode modelNode = ParseNode(childNode);
                switch (modelNode.Type)
                {
                    case "Node":
                        Nodes.Add(modelNode);
                        break;
                    case "MacroNode":
                        MacroNodes.Add(modelNode);
                        break;
                    case "CenterOfMass":
                        CenterOfMass.Add(modelNode);
                        break;
                }
                if (modelNode.IsCollisible)
                {
                    CollisibleNodes.Add(modelNode);
                }
                modelNode.Id = _NodesAll.Count;
                _NodesAll.Add(modelNode);
                render.Add(modelNode);
            }
        }

        private ModelNode ParseNode(XmlNode node)
        {
            float p_x = node.Attributes["X"].ParseFloat();
            float num = node.Attributes["Y"].ParseFloat();
            float p_z = node.Attributes["Z"].ParseFloat();
            string value = node.Attributes["Type"].Value;
            MacroNode p_macroNode = !(value == "MacroNode") ? null : new MacroNode();
            ModelNode modelNode = new ModelNode(new Vector3(p_x, 0f - num, p_z), p_macroNode);
            modelNode.Name = node.Name;
            modelNode.Type = value;
            modelNode.Weight = node.Attributes["Mass"].ParseFloat();
            modelNode.IsFixed = node.Attributes["Fixed"].ParseBool();
            modelNode.IsCollisible = node.Attributes["Collisible"].ParseBool();
            modelNode.IsPhysics = node.Attributes["Cloth"].ParseBool();
            modelNode.Attenuation = node.Attributes["Attenuation"].ParseFloat();
            switch (modelNode.Type)
            {
                case "CenterOfMass":
                    {
                        int num2 = node.Attributes["NodesCount"].ParseInt();
                        if (num2 != 0)
                        {
                            modelNode.MacroNode = new MacroNode();
                            for (int j = 0; j < num2; j++)
                            {
                                string value3 = node.Attributes["ChildNode" + (j + 1)].Value;
                                modelNode.MacroNode.ChildNode.Add(GetNode(value3));
                            }
                        }
                        break;
                    }
                case "MacroNode":
                    {
                        int num2 = node.Attributes["NodesCount"].ParseInt();
                        if (num2 != 0)
                        {
                            for (int i = 0; i < num2; i++)
                            {
                                string value2 = node.Attributes["ChildNode" + (i + 1)].Value;
                                modelNode.MacroNode.ChildNode.Add(GetNode(value2));
                                modelNode.MacroNode.LCC.Add(node.Attributes["LCC" + (i + 1)].ParseFloat());
                            }
                        }
                        break;
                    }
            }
            return modelNode;
        }

        public void ParseEdges(XmlNode node, ModelRender render)
        {
            if (node == null)
            {
                return;
            }
            foreach (XmlNode childNode in node.ChildNodes)
            {
                ModelLine modelLine = ParseEdge(childNode);
                switch (modelLine.Type)
                {
                    case "Edge":
                        Edges.Add(modelLine);
                        break;
                    case "Muscle":
                        Muscules.Add(modelLine);
                        break;
                }
                if (modelLine.Collisible)
                {
                    CollisibleEdges.Add(modelLine);
                }
                if (modelLine.Start.IsPhysics || modelLine.End.IsPhysics)
                {
                    EdgesPhysic.Add(modelLine);
                }
                EdgesAll.Add(modelLine);
                render.Add(modelLine);
            }
        }

        private ModelLine ParseEdge(XmlNode node)
        {
            string value = node.Attributes["End1"].Value;
            string value2 = node.Attributes["End2"].Value;
            ModelLine modelLine = new ModelLine(GetNode(value), GetNode(value2));
            modelLine.Name = node.Name;
            modelLine.Type = node.Attributes["Type"].Value;
            modelLine.Length = node.Attributes["Length"].ParseFloat();
            modelLine.Collisible = node.Attributes["Collisible"].ParseBool();
            return modelLine;
        }

        public void ParseCapsules(XmlNode node, ModelRender render)
        {
            if (node == null)
            {
                return;
            }
            foreach (XmlNode childNode in node.ChildNodes)
            {
                ParseCapsule(childNode, render);
            }
        }

        private void ParseCapsule(XmlNode node, ModelRender render)
        {
            switch (node.Attributes["Type"].Value)
            {
                case "Capsule":
                    var modelLine = new ModelLine(GetEdge(node.Attributes["Edge"].Value));
                    modelLine.Stroke = node.Attributes["Radius1"].ParseFloat();
                    modelLine.Margin1 = node.Attributes["Margin1"].ParseFloat();
                    modelLine.Margin2 = node.Attributes["Margin2"].ParseFloat();
                    modelLine.Type = "Capsule";
                    Capsules.Add(modelLine);
                    render.Add(modelLine);
                    break;
                case "Triangle":
                    var triangle = new Triangle();
                    triangle.Node0 = GetNode(node.Attributes["Node1"].Value);
                    triangle.Node1 = GetNode(node.Attributes["Node2"].Value);
                    triangle.Node2 = GetNode(node.Attributes["Node3"].Value);
                    render.Add(triangle);
                    Triangles.Add(triangle);
                    break;
            }
        }

        public void PositionToPivot(ModelNode node)
        {
            if (node != null)
            {
                Vector3 Vector3 = new Vector3(-1f, -1f, 0);
                node.Start = node.Start.Set(PivotNode.Start + Vector3);
                node.End = node.End.Set(PivotNode.End + Vector3);
            }
        }

        public void Position(Vector3 vector, string name = "NPivot")
        {
            ModelNode node = GetNode(name);
            if (node == null)
            {
                return;
            }
            Vector3 p_vector2 = vector - node.Start;
            Vector3 p_vector3 = vector - node.End;
            foreach (ModelNode item in _NodesAll)
            {
                item.Start = item.Start.Add(p_vector2);
                item.End = item.End.Add(p_vector3);
            }
            if (DetectorHorizontalLine != null)
            {
                DetectorHorizontalLine.Reset();
            }
            if (DetectorVerticalLine != null)
            {
                DetectorVerticalLine.Reset();
            }
        }

        private void CreateBothNodeList()
        {
            _BothNodeList = new List<int[]>();
            ModelNode modelNode;
            for (int i = 0; i < _NodesAll.Count; i++)
            {
                modelNode = _NodesAll[i];
                if (modelNode.BothIndex != 0 && !IsBothNodes(modelNode.Id, _BothNodeList))
                {
                    _BothNodeList.Add(new[]
                    {
                        modelNode.Id,
                        BothNode(modelNode, _NodesAll).Id
                    });
                }
            }
        }

        private static bool IsBothNodes(int p_id, List<int[]> p_list)
        {
            for (int i = 0; i < p_list.Count; i++)
            {
                int[] array = p_list[i];
                if (p_id == array[0] || p_id == array[1])
                {
                    return true;
                }
            }
            return false;
        }

        private static ModelNode BothNode(ModelNode p_node, List<ModelNode> p_nodes)
        {
            foreach (var node in p_nodes)
            {
                if (node.Name != p_node.Name && node.BothName == p_node.BothName)
                {
                    return node;
                }
            }
            return null;
        }

        public ModelNode GetNode(string name = "NPivot")
        {
            foreach (var node in _NodesAll)
            {
                if (node.Name == name)
                {
                    return node;
                }
            }
            return null;
        }

        public ModelNode GetNodeToLow(string name)
        {
            for (int i = 0; i < _NodesAll.Count; i++)
            {
                if (_NodesAll[i].Name.ToLower() == name)
                {
                    return _NodesAll[i];
                }
            }
            return null;
        }

        public ModelNode GetNode(int p_id)
        {
            if (_NodesAll.Count <= p_id)
            {
                return null;
            }
            return _NodesAll[p_id];
        }

        public int GetNodeIdByName(string name = "NPivot")
        {
            return GetNode(name).Id;
        }

        public ModelLine GetEdge(string name)
        {
            foreach (ModelLine item in EdgesAll)
            {
                if (item.Name == name)
                {
                    return item;
                }
            }
            return null;
        }

        public Vector3 Position(string name = "NPivot", bool isCurrent = true)
        {
            ModelNode node = GetNode(name);
            if (node != null)
            {
                return !isCurrent ? node.End : node.Start;
            }
            return default;
        }

        public void Reset()
        {
            foreach (var node in _NodesAll)
            {
                node.Reset();
            }
        }

        /// <summary>Every file's capsules and triangles, in load order (the game's ModelRender.Draw for each skin).</summary>
        public void Draw(ModelCanvas canvas)
        {
            foreach (var render in _Renders)
            {
                render.Draw(canvas);
            }
        }

        public void RenderMacroNode()
        {
            foreach (var macronode in MacroNodes)
            {
                macronode.MacroNodeCompute();
            }
        }

        public void RenderDetector()
        {
            DetectorVerticalLine.Update();
            DetectorHorizontalLine.Update();
        }

        public Vector2 GetPositionForIcon()
        {
            if (_botIconPosition == null)
            {
                foreach (var render in _Renders)
                {
                    _botIconPosition = render.GetCapsulTransform("EChest");
                }
            }
            else
            {
                return Vector2.Zero;
                //FIX

            }

            return Vector2.Zero;
        }
    }
}
