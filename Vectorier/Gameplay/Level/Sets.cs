// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;
using System.Globalization;
using System.Xml;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Runners.Animation;
using Vectorier.Gameplay.User;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Level
{
    public class Sets
    {
        private List<XmlNode> _XmlObjects = new List<XmlNode>();

        private List<XmlNode> _xmlBuilds = new List<XmlNode>();

        private HashSet<string> _filesBuilds = new HashSet<string>();

        private HashSet<string> _filesObjects = new HashSet<string>();

        private XmlDocument _document;

        protected Dictionary<string, string> _ChoisesDictionary = new Dictionary<string, string>();

        protected Dictionary<float, VisualContainer> _Containers = new Dictionary<float, VisualContainer>();

        protected List<Runner> _allRunners = new List<Runner>();

        public Dictionary<string, XmlNode> _ObjectsNodes = new Dictionary<string, XmlNode>();

        protected List<UserData> _UserData = new List<UserData>();

        protected string _music;

        protected List<ObjectRunner> _Objects = new List<ObjectRunner>();

        public List<VisualRunner> Visuals = new List<VisualRunner>();

        public List<PlatformRunner> Platforms = new List<PlatformRunner>();

        public List<TriggerRunner> Triggers = new List<TriggerRunner>();

        public List<AreaRunner> Areas = new List<AreaRunner>();

        public List<AnimationRunner> Animations = new List<AnimationRunner>();

        public List<ParticleRunner> Particles = new List<ParticleRunner>();

        public List<ItemRunner> Items = new List<ItemRunner>();

        public List<TrapezoidRunner> Trapezoids = new List<TrapezoidRunner>();

        public List<CameraRunner> Cameras = new List<CameraRunner>();

        public List<SpawnRunner> Spawns = new List<SpawnRunner>();

        public List<QuadRunner> Quads = new List<QuadRunner>();

        public List<QuadRunner> QuadsAll = new List<QuadRunner>();

        public List<PrimitiveRunner> Primitives = new List<PrimitiveRunner>();

        public static Sets Current
        {
            get;
            protected set;
        }

        public Dictionary<float, VisualContainer> Containers => _Containers;

        public List<Runner> AllRunners => _allRunners;

        public int TotalCoins
        {
            get;
            protected set;
        }

        public List<UserData> UserData => _UserData;

        public string Music => _music;

        public int totalPoints
        {
            get
            {
                int points = 0;
                foreach (var area in Areas)
                {
                    if (area is TrickAreaRunner trick)
                    {
                        points += trick.score;
                    }
                }
                foreach (var item in Items)
                {
                    if (item is ItemScoreRunner scoreRunner)
                    {
                        points += scoreRunner.score;
                    }
                }
                return points;
            }
        }

        public int totalTricks
        {
            get
            {
                int index = 0;
                foreach (var area in Areas)
                {
                    if (area is TrickAreaRunner)
                    {
                        index++;
                    }
                }
                return index;
            }
        }

        public int totalBonus
        {
            get
            {
                int index = 0;
                foreach (var item in Items)
                {
                    if (item.Type == 0)
                    {
                        index++;
                    }
                }
                return index;
            }
        }

        public Sets(XmlDocument document)
        {
            Current = this;
            _document = document;
            LoadLevel();
        }

        private void LoadLevel()
        {
            var root = _document["Root"];
            if (root["Sets"] != null)
            {
                foreach (XmlNode set in root["Sets"])
                {
                    if (set.Name == "City")
                    {
                        _filesBuilds.Add(set.Attributes["FileName"].Value);
                    }
                    else
                    {
                        _filesObjects.Add(set.Attributes["FileName"].Value);
                    }
                }
            }
            LoadBuilds();
            LoadObjects();
        }

        private static XmlDocument[] ParallelLoad(List<string> files)
        {
            var documents = new XmlDocument[files.Count];
            Parallel.For(0, files.Count, i => documents[i] = XmlUtils.OpenXMLDocument(VectorPaths.Xml, files[i]));
            return documents;
        }

        private void LoadBuilds()
        {
            // Read and parse the files in parallel, then use them in their original order.
            var buildFiles = _filesBuilds.ToList();
            var documents = ParallelLoad(buildFiles);
            for (int f = 0; f < buildFiles.Count; f++)
            {
                var document = documents[f];
                if (document == null)
                    continue;
                if (document["Root"] != null && document["Root"]["Objects"] != null)
                {
                    _xmlBuilds.Add(document["Root"]["Objects"]);
                }
                if (document["Root"] != null && document["Root"]["Sets"] != null)
                {
                    foreach (XmlNode set in document["Root"]["Sets"])
                    {
                        if (!_filesObjects.Contains(set.Attributes["FileName"].Value))
                            _filesObjects.Add(set.Attributes["FileName"].Value);
                    }
                }
            }
        }

        private void LoadObjects()
        {
            var objectFiles = _filesObjects.ToList();
            var documents = ParallelLoad(objectFiles);
            for (int f = 0; f < objectFiles.Count; f++)
            {
                var file = objectFiles[f];
                var document = documents[f];
                var root = document?["Root"];
                if (root == null)
                {
                    Console.WriteLine("Empty node from file \"" + file + "\"!");
                    continue;
                }
                _XmlObjects.Add(root["Objects"]);
            }
            Parse();
        }

        private void Parse()
        {
            _ObjectsNodes = new Dictionary<string, XmlNode>();
            ParseChoices(_document["Root"]["Choises"]);
            ParseObjects();
            ParseBuilds();
            ParseLevel();
            _ObjectsNodes = null;
        }

        public void ParseChoices(XmlNode nodes)
        {
            if (nodes != null)
            {
                foreach (XmlNode node in nodes)
                {
                    _ChoisesDictionary[node.Attributes["Name"].Value] = node.Attributes["Variant"].Value;
                }
            }
            string mode = "CommonMode";
            if (LevelMainController.IsHunterMode)
            {
                mode = "HunterMode";
            }
            _ChoisesDictionary["AITriggers"] = mode;
        }

        public void ParseObjects()
        {
            foreach (XmlNode node in _XmlObjects)
            {
                foreach (XmlNode obj in node)
                {
                    if (obj.Attributes["Name"] != null)
                        _ObjectsNodes[obj.Attributes["Name"].Value] = obj;
                }

            }
        }

        public void ParseBuilds()
        {
            foreach (XmlNode node in _xmlBuilds)
            {
                foreach (XmlNode build in node)
                {
                    _ObjectsNodes[build.Attributes["Name"].Value] = build;
                }
            }

        }

        public void ParseLevel()
        {
            var root = _document["Root"];
            TotalCoins = root["Coins"].Attributes["Value"].ParseInt();
            ParseModels(root);
            AddMusics(root["Music"]);
            CreateObjects();
            GetElements(_Objects);
            InitRunners();
            InitItems();
            InitTriggers();
            InitParticles();
            InitAnimations();
            InitPrimitives();
            InitAreas();
            InitVisual();
        }

        protected void ParseModels(XmlNode nodes)
        {
            ParseModels(nodes.ChildNodes.Cast<XmlNode>().Where(node => node.Name == "Models"));
        }

        protected void ParseModels(IEnumerable<XmlNode> nodes)
        {
            foreach (XmlNode node in nodes)
            {
                if (node.Attributes["Choice"] == null)
                {
                    AddModels(node);
                    return;
                }
                string choice = node.Attributes["Choice"].Value;
                string variant = node.Attributes["Variant"].Value;

                if (_ChoisesDictionary[choice] == variant)
                {
                    AddModels(node);
                }
            }
        }

        public void AddMusics(XmlNode node)
        {
            _music = node.Attributes["Name"].Value;
        }

        public void CreateObjects()
        {
            XmlNode root = _document["Root"]["Track"];
            List<float> factors = new List<float>();
            foreach (XmlNode xmlNode in root)
            {
                if (xmlNode.Attributes["Factor"] != null)
                {
                    factors.Add(float.Parse(xmlNode.Attributes["Factor"].Value, CultureInfo.InvariantCulture.NumberFormat));
                }
            }
            factors.Sort();
            uint num = 0;
            for (int i = 0; i < root.ChildNodes.Count; i++)
            {
                var childNode = root.ChildNodes[i];
                ObjectRunner objectRunner = null;
                string name = string.Empty;
                if (childNode.Attributes["Name"] != null)
                {
                    name = childNode.Attributes["Name"].Value;
                }

                objectRunner = new ObjectRunner(null);
                objectRunner.Parse(childNode, null, _ChoisesDictionary);
                objectRunner.Init();

                _Objects.Add(objectRunner);
                VisualContainer visualContainer = null;
                if (!_Containers.ContainsKey(objectRunner.Factor))
                {
                    visualContainer = new VisualContainer(objectRunner.Factor, (factors.Count - factors.IndexOf(objectRunner.Factor)) * 10);
                    _Containers[objectRunner.Factor] = visualContainer;
                }
                else
                {
                    visualContainer = _Containers[objectRunner.Factor];
                }
                objectRunner.SetLayer(visualContainer);
            }
        }

        public static XmlNode ObjectNode(string name)
        {
            if (Current != null && Current._ObjectsNodes.ContainsKey(name))
            {
                return Current._ObjectsNodes[name];
            }
            Console.WriteLine("Object not find: " + name);
            return null;
        }

        public void AddModels(XmlNode Nodes)
        {
            foreach (XmlNode node in Nodes.ChildNodes)
            {
                _UserData.Add(GetUserData(node));
            }
        }

        public void InitRunners()
        {
            //foreach (var runner in AllRunners)
            //{
            //    //runner.InitRunner(null, Xml2PrefabRoot.Serialize);
            //}
        }

        public void InitItems()
        {
            foreach (var item in Items)
            {
                item.Init();
            }
        }


        public void InitTriggers()
        {
            foreach (var trigger in Triggers)
            {
                trigger.Init();
            }
        }

        public void InitParticles()
        {
            foreach (var particle in Particles)
            {
                particle.Init();
            }
        }

        public void InitAnimations()
        {
            foreach (var animation in Animations)
            {
                animation.Init();
            }
        }

        public void InitPrimitives()
        {
            foreach (var primitive in Primitives)
            {
                primitive.Load();
            }
        }

        public void InitVisual()
        {
            // Decode every PNG the level shows on all cores first; the GPU upload then only copies pixels.
            ResourceManager.PrefetchImages(Visuals.Select(v => v.Name));
            foreach (var visual in Visuals)
            {
                visual.Init();
            }
            ResourceManager.ReleasePrefetched();
        }

        public void InitAreas()
        {
            foreach (var area in Areas)
            {
                area.Init();
            }
        }

        public void GetElements(List<ObjectRunner> objectRunners)
        {
            foreach (var objectRunner in objectRunners)
            {
                foreach (var runner in objectRunner.Element.Runners)
                {
                    _allRunners.Add(runner);
                }
                Add(objectRunner.Element);
                GetElements(objectRunner.Childs);
            }
        }

        public void Add(Elements elements)
        {
            Visuals.AddRange(elements.Visuals);
            Platforms.AddRange(elements.Platforms);
            Triggers.AddRange(elements.Triggers);
            Areas.AddRange(elements.Areas);
            Spawns.AddRange(elements.Spawns);
            Cameras.AddRange(elements.Cameras);
            Trapezoids.AddRange(elements.Trapezoids);
            Primitives.AddRange(elements.Primitives);
            Animations.AddRange(elements.Animations);
            Particles.AddRange(elements.Particles);
            Items.AddRange(elements.Items);

            Quads.AddRange(elements.Platforms);
            QuadsAll.AddRange(elements.Platforms);

            QuadsAll.AddRange(elements.Triggers);
            QuadsAll.AddRange(elements.Areas);

            Quads.AddRange(elements.Trapezoids);
            QuadsAll.AddRange(elements.Trapezoids);

        }

        public static UserData GetUserData(XmlNode node)
        {
            var userData = new UserData("");
            userData.Name = node.Attributes["Name"].Value;
            userData.BirthSpawn = node.Attributes["BirthSpawn"].ParseString("");
            userData.isIcon = node.Attributes["Icon"].ParseBool();
            userData.Color = node.Attributes["Color"] == null ? Color.Black : ColorUtils.FromHex(node.Attributes["Color"].Value);
            userData.Skins = node.Attributes["Skins"] == null ? new List<string>() : new List<string>(node.Attributes["Skins"].Value.Split('|'));
            userData.Stocks = node.Attributes["Stocks"] == null ? new List<string>() : new List<string>(node.Attributes["Stocks"].Value.Split('|'));
            userData.Arrests = node.Attributes["Arrests"] == null ? new List<string>() : new List<string>(node.Attributes["Arrests"].Value.Split('|'));
            userData.Murders = node.Attributes["Murders"] == null ? new List<string>() : new List<string>(node.Attributes["Murders"].Value.Split('|'));
            userData.Respawns = node.Attributes["Respawns"] == null ? new List<string>() : new List<string>(node.Attributes["Respawns"].Value.Split('|'));
            userData.Births = node.Attributes["AllowedSpawns"] == null ? new List<string>() : new List<string>(node.Attributes["AllowedSpawns"].Value.Split('|'));
            userData.IsSelf = node.Attributes["Type"].ParseBool();
            userData.IsTrick = node.Attributes["Trick"].ParseBool();
            userData.IsItem = node.Attributes["Item"].ParseBool();
            userData.IsVictory = node.Attributes["Victory"].ParseBool();
            userData.IsLost = node.Attributes["Lose"].ParseBool();
            userData.AI = int.Parse(node.Attributes["AI"].Value, System.Globalization.CultureInfo.InvariantCulture);
            userData.StartTime = node.Attributes["Time"].ParseFloat();
            userData.LiveTime = node.Attributes["LifeTime"].ParseFloat(2);
            if (userData.Skins.Count == 0)
            {
                userData.Skins.Add("1");
            }
            userData.Init();
            return userData;
        }
    }
}
