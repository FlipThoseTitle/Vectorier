// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Location;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Runners
{
    public class AreaRunner : QuadRunner
    {
        public enum AreaType
        {
            None,
            Trick,
            Help,
            Catch
        }

        private AreaType _Type;

        private readonly XmlNode _node;

        protected string _TypeName;

        /// <summary>The XML Type ("Trick", "Help", "Catch", an animation type...).</summary>
        public string TypeName => _TypeName;

        public bool IsNone => _Type == AreaType.None;

        public bool IsHelp => _Type == AreaType.Help;

        public bool IsTrick => _Type == AreaType.Trick;

        public bool IsCatch => _Type == AreaType.Catch;

        public AreaRunner(AreaType type, float x, float y, float width, float height, string typeName, string name)
            : base(x, y, width, height, false, string.IsNullOrEmpty(typeName) ? 1 : 0, name)
        {
            _Type = type;
            _TypeName = typeName;

            TypeClass = RunnerType.Area;
            LoadBinaryIfTrick();
        }

        /// <summary>A Trick area's move is loaded with the level (and again after Reload Moves).</summary>
        public void LoadBinaryIfTrick()
        {
            if (Animations.Animation.ContainsKey(Name.Replace("Trigger", "")))
            {
                AnimationInfo animationInfo = Animations.Animation[Name.Replace("Trigger", "")];
                if (animationInfo.IsTrick)
                {
                    AnimationTrickInfo.LoadAnimation(animationInfo as AnimationTrickInfo);
                }
            }
        }

        public virtual void Init()
        {

        }

        public override void Draw()
        {
            // Drawing is done by the editor (Controls/Scene/SceneView.Gameplay.cs).
        }

        public virtual bool OnKeyClick(KeyVariables Key)
        {
            return true;
        }

        public virtual void Activate(ModelHuman model)
        {
        }

        public virtual void Deactivate(ModelHuman model)
        {
        }
    }
}
