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
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Animation
{
    public class AnimationReaction
    {
        public const string NameDeath = "Death";

        private AnimationInfo _info;
        private int _infoVersion;

        /// <summary>Raised by each Reload Moves: reactions look their move up again.</summary>
        public static int MovesVersion;

        private readonly List<int> _slopeH = new List<int>();

        private readonly List<int> _slopeV = new List<int>();

        private readonly int _triggerNameHash;

        private static System.Random _Random;

        /// <summary>SlopeH / SlopeV platform types the reaction accepts (empty: any).</summary>
        /// <summary>The reaction's attributes as written in moves.xml (FirstFrame="21" Sides="3"...).</summary>
        public string XmlAttributes { get; } = string.Empty;

        /// <summary>The ReactionGroup this reaction is defined in (null: written directly in an event).</summary>
        public string? ReactionGroup => AnimationGroup.Groups.Values.FirstOrDefault(g => g.Reactions != null && g.Reactions.Contains(this))?.Name;

        public IReadOnlyList<int> SlopeH => _slopeH;

        public IReadOnlyList<int> SlopeV => _slopeV;

        public static List<AnimationReaction> Reactions
        {
            get;
        }

        public string Name
        {
            get;
            set;
        }

        public int FirstFrame
        {
            get;
            set;
        }

        public int Priority
        {
            get;
            set;
        }

        public List<int> Sides
        {
            get;
            set;
        }

        public AnimationDeltas DeltasH
        {
            get;
        }

        public AnimationDeltas DeltasV
        {
            get;
        }

        public AnimationDeltas DeltasC
        {
            get;
        }

        public bool CornerCling
        {
            get;
            set;
        }

        public int CornerClingV
        {
            get;
            set;
        }

        public int CornerClingH
        {
            get;
            set;
        }

        public string PivotNode
        {
            get;
            set;
        }

        /// <summary>Safe / SafeH / SafeV written on the reaction (null: not written, the interval's value applies).</summary>
        public bool? SafeAttribute { get; }

        public bool? SafeHAttribute { get; }

        public bool? SafeVAttribute { get; }

        public bool SafeHorizontal
        {
            get;
            set;
        }

        public bool SafeVertical
        {
            get;
            set;
        }

        public bool Mirror
        {
            get;
        }

        public bool Reverse
        {
            get;
            set;
        }

        public float InsideH
        {
            get;
            set;
        }

        public float InsideV
        {
            get;
            set;
        }

        public string TriggerName
        {
            get;
        }

        public List<string> NodesWi
        {
            get;
            set;
        }

        public bool OnEndTrigger
        {
            get;
        }

        private AnimationInfo Info
        {
            get
            {
                // After Reload Moves the move is looked up again (reactions of the level, e.g. a spawn's, outlive it).
                if (_info == null || _infoVersion != MovesVersion)
                {
                    _info = Animations.Animation[Name];
                    _infoVersion = MovesVersion;
                }
                return _info;
            }
        }

        public bool IsAnimationArrest
        {
            get
            {
                if (Animations.Animation.ContainsKey(Name) && Animations.Animation[Name].Type == 5)
                {
                    return true;
                }
                return false;
            }
        }

        public bool IsTrick
        {
            get
            {
                if (Animations.Animation.ContainsKey(Name) && Animations.Animation[Name].Type == 3)
                {
                    return true;
                }
                return false;
            }
        }

        static AnimationReaction()
        {
            Reactions = new List<AnimationReaction>();
            _Random = new System.Random();
        }

        public AnimationReaction(string name, int frame)
        {
            Sides = new List<int>();
            NodesWi = new List<string>();

            Name = name;
            FirstFrame = frame;
        }

        public AnimationReaction(XmlNode node)
        {
            Sides = new List<int>();
            NodesWi = new List<string>();
            Name = node.Name;
            XmlAttributes = node.Attributes == null ? string.Empty :
                string.Join(" ", node.Attributes.Cast<XmlAttribute>().Select(a => $"{a.Name}=\"{a.Value}\""));
            FirstFrame = node.Attributes["FirstFrame"].ParseInt();
            Priority = node.Attributes["Priority"].ParseInt();
            CornerCling = node.Attributes["CornerCling"].ParseBool();
            CornerClingV = node.Attributes["CornerClingV"].ParseInt(-1);
            CornerClingH = node.Attributes["CornerClingH"].ParseInt(-1);
            SafeHorizontal = node.Attributes["SafeH"].ParseBool();
            SafeVertical = node.Attributes["SafeV"].ParseBool();
            SafeAttribute = node.Attributes["Safe"] == null ? null : node.Attributes["Safe"].ParseBool();
            SafeHAttribute = node.Attributes["SafeH"] == null ? null : SafeHorizontal;
            SafeVAttribute = node.Attributes["SafeV"] == null ? null : SafeVertical;
            Reverse = node.Attributes["Reverse"].ParseBool();
            Mirror = node.Attributes["Mirror"].ParseBool();
            OnEndTrigger = node.Attributes["OnEndTrigger"].ParseBool();
            PivotNode = node.Attributes["PivotNode"].ParseString();
            if (node.Attributes["TriggerName"] != null)
            {
                TriggerName = node.Attributes["TriggerName"].Value;
                _triggerNameHash = TriggerName.GetHashCode();
            }
            if (node.Attributes["AreaName"] != null)
            {
                TriggerName = node.Attributes["AreaName"].Value;
                _triggerNameHash = TriggerName.GetHashCode();
            }
            if (node.Attributes["Sides"] != null)
            {
                string[] sides = node.Attributes["Sides"].Value.Split('|');
                foreach (string side in sides)
                {
                    if (!string.IsNullOrEmpty(side))
                        Sides.Add(int.Parse(side, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            if (node.Attributes["SlopeH"] != null)
            {
                string[] array2 = node.Attributes["SlopeH"].Value.Split('|');
                foreach (string s2 in array2)
                {
                    if (!string.IsNullOrEmpty(s2))
                        _slopeH.Add(int.Parse(s2, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            if (node.Attributes["SlopeV"] != null)
            {
                string[] array3 = node.Attributes["SlopeV"].Value.Split('|');
                foreach (string s3 in array3)
                {
                    if (!string.IsNullOrEmpty(s3))
                        _slopeV.Add(int.Parse(s3, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            if (node.Attributes["NodesWI"] != null)
            {
                NodesWi = new List<string>(node.Attributes["NodesWI"].Value.Split('|'));
            }
            InsideH = node.Attributes["InsideH"].ParseFloat(float.NaN);
            InsideV = node.Attributes["InsideV"].ParseFloat(float.NaN);
            DeltasH = new AnimationDeltas(node, AnimationDeltaType.Horizontal);
            DeltasV = new AnimationDeltas(node, AnimationDeltaType.Vertical);
            DeltasC = new AnimationDeltas(node, AnimationDeltaType.Collision);
            Reactions.Add(this);
            _info = null;
        }

        public bool CheckNameHash(int hash)
        {
            return _triggerNameHash == hash;
        }

        public static bool IsTriggerType(AnimationReaction reaction, AreaRunner area, ModelHuman model)
        {
            if (area.IsTrick)
            {
                if ((area as TrickAreaRunner).isActive && model.IsTrick)
                {
                    return reaction.CheckNameHash(area.NameHash);
                }
                return false;
            }
            if (area.IsCatch)
            {
                if (area.IsEnabled)
                {
                    return (area as ArrestAreaRunner).IsArrest && reaction.CheckNameHash(area.NameHash);
                }
                return false;
            }
            return reaction.CheckNameHash(area.NameHash);
        }

        public static AnimationReaction GetReaction(List<List<AnimationReaction>> Reactions, AnimationDeltaData DeltaH, AnimationDeltaData DeltaV, AnimationDeltaData DeltaC, List<AreaRunner> Areas, ModelHuman Model, Vector3 Velocity)
        {
            var sort = ToList(Reactions);
            sort = Sort(sort, Model);
            sort = Sort(sort, Areas, Model);
            sort = Sort(sort, DeltaH, DeltaV, DeltaC, Model);
            sort = Sort(sort, DeltaH, DeltaV, DeltaC, Model.Sign, Velocity);
            sort = Sort(sort);
            return Random(sort);
        }

        public static List<AnimationReaction> ToList(List<List<AnimationReaction>> Animations)
        {
            List<AnimationReaction> list = new List<AnimationReaction>();
            foreach (var animation in Animations)
            {
                if (animation != null)
                    list.AddRange(animation);
            }
            return list;
        }

        public static List<AnimationReaction> Sort(List<AnimationReaction> Reactions, List<AreaRunner> Areas, ModelHuman Model)
        {
            for (int num = Reactions.Count - 1; num >= 0; num--)
            {
                if (Reactions[num].TriggerName != null)
                {
                    bool flag = false;
                    foreach (AreaRunner p_area in Areas)
                    {
                        if (IsTriggerType(Reactions[num], p_area, Model))
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (!flag)
                    {
                        Reactions.Remove(Reactions[num]);
                    }
                }
            }
            return Reactions;
        }

        public static List<AnimationReaction> Sort(List<AnimationReaction> reactions, ModelHuman model)
        {
            DetectorLine detectorHorizontalLine = model.ModelObject.DetectorHorizontalLine;
            DetectorLine detectorVerticalLine = model.ModelObject.DetectorVerticalLine;
            for (int num = reactions.Count - 1; num >= 0; num--)
            {
                if (!reactions[num].IsInside(detectorHorizontalLine, detectorVerticalLine, model.Sign))
                {
                    reactions.RemoveAt(num);
                }
            }
            return reactions;
        }

        public static List<AnimationReaction> Sort(List<AnimationReaction> Reactions, AnimationDeltaData DeltaH, AnimationDeltaData DeltaV, AnimationDeltaData DeltaС, ModelHuman Model)
        {
            for (int i = Reactions.Count - 1; i >= 0; i--)
            {
                var reaction = Reactions[i];
                if (!reaction.IsSlope(DeltaH, DeltaV, DeltaС, Model.Sign))
                {
                    Reactions.RemoveAt(i);
                }
            }
            return Reactions;
        }

        public static List<AnimationReaction> Sort(List<AnimationReaction> Reactions, AnimationDeltaData DeltaH, AnimationDeltaData DeltaV, AnimationDeltaData DeltaС, int Sign, Vector3 Velocity)
        {
            for (int i = Reactions.Count - 1; i >= 0; i--)
            {
                var reaction = Reactions[i];
                if (!reaction.IsDeltaCheck(DeltaH, DeltaV, DeltaС, Sign, Velocity))
                {
                    Reactions.RemoveAt(i);
                }
            }
            return Reactions;
        }

        public static List<AnimationReaction> Sort(List<AnimationReaction> Reactions)
        {
            var maxPriority = MaxPriority(Reactions);
            for (int i = Reactions.Count - 1; i >= 0; i--)
            {
                var reaction = Reactions[i];
                if (reaction.Priority != maxPriority)
                {
                    Reactions.RemoveAt(i);
                }
            }
            return Reactions;
        }

        public static AnimationReaction Random(List<AnimationReaction> Reactions)
        {
            return Reactions.Count != 0 ? Reactions[_Random.Next(0, Reactions.Count - 1)] : null;
        }

        public static int MaxPriority(List<AnimationReaction> Reactions)
        {
            int num = 0;
            for (int i = 0; i < Reactions.Count; i++)
            {
                num = System.Math.Max(Reactions[i].Priority, num);
            }
            return num;
        }

        public bool IsSide(int Value, int Sign)
        {
            if (Sides.Count == 0 || Value == -1)
            {
                return true;
            }
            for (int i = 0; i < Sides.Count; i++)
            {
                int num = Sides[i];
                if (Sign == -1)
                {
                    switch (num)
                    {
                        case 1:
                            num = 3;
                            break;
                        case 3:
                            num = 1;
                            break;
                    }
                }
                if (num == Value)
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsSlope(AnimationDeltaData DeltaH, AnimationDeltaData DeltaV, AnimationDeltaData DeltaC, int Sign)
        {
            return IsDeltaSlope(DeltaH, _slopeH, Sign) && IsDeltaSlope(DeltaV, _slopeV, Sign);
        }

        public bool IsDeltaSlope(AnimationDeltaData Delta, List<int> Slopes, int Sign)
        {
            if (Delta == null || Slopes == null || Slopes.Count == 0)
            {
                return true;
            }
            int type = Delta.Platform.Type;
            foreach (int p_slope in Slopes)
            {
                int num;
                if (Sign == 1)
                {
                    num = p_slope;
                }
                else
                {
                    switch (p_slope)
                    {
                        case 1:
                            num = 2;
                            break;
                        case 2:
                            num = 1;
                            break;
                        default:
                            num = p_slope;
                            break;
                    }
                }
                if (num == type)
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsInside(DetectorLine DetectorH, DetectorLine DetectorV, int Sign)
        {
            return Inside(InsideH, DetectorH) && Inside(InsideV, DetectorV);
        }

        public bool Inside(float Inside, DetectorLine Detector)
        {
            if (float.IsNaN(Inside))
            {
                return true;
            }
            if (Detector.Platform == null)
            {
                return Inside == 0f;
            }
            return Inside == 1f;
        }

        public bool IsDeltaCheck(AnimationDeltaData deltaH, AnimationDeltaData deltaV, AnimationDeltaData deltaС, int Sign, Vector3 Velocity)
        {
            bool flag = DeltasH.IsCheck(deltaH, Sign, Velocity);
            bool flag2 = DeltasV.IsCheck(deltaV, Sign, Velocity);
            bool flag3 = DeltasC.IsCheck(deltaС, Sign, Velocity);
            return flag && flag2 && flag3;
        }
    }
}
