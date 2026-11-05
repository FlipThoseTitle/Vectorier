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
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners.Animation;

namespace Vectorier.Gameplay.Runners
{
    //FIX
    public class TrickAreaRunner : AreaRunner
    {
        private const float ExpansionWidthRatio = 1f;

        private readonly float _originalWidth;

        private bool _wasExpanded;

        private readonly XmlNode _node;

        public static TrickAreaRunner Current;

        private bool _isActive;

        private string _itemName;

        private int _score;

        public bool isActive => _isActive;

        public string itemName => _itemName;

        public int score => _score;

        AnimationSprite Idle;

        AnimationSprite Active;

        ImageRendering Image;

        public TrickAreaRunner(float x, float y, float width, float height, string typeName, string name, string itemName, int score)
            : base(AreaType.Trick, x, y, width, height, typeName, name)
        {
            _itemName = itemName;
            _score = score;
            _originalWidth = width;
            _isActive = true;

            //LocalScale = new Vector2(2, 1);

        }

        public override void Activate(ModelHuman model)
        {
            if (model.UserData.IsTrick)
            {
                Current = this;
            }
        }

        public override void Deactivate(ModelHuman model)
        {
            if (model.UserData.IsTrick)
            {
                Current = null;
            }
        }

        public static void TrickGetByModel(ModelHuman model)
        {
            if (Current == null || !model.UserData.IsSelf)
            {
                return;
            }
            SoundsManager.PlaySounds(SoundType.trick_activate);
            model.CollectTrick(Current);
            Current.RunActivate();
            //LevelMainController.current.levelSceneController.ShowActivatedTrick(Current);
        }

        public override void Init()
        {
            base.Init();
            InitActiveState();
        }

        public override void Reset()
        {
            base.Reset();
            Current = null;
            _isActive = true;
            _wasExpanded = false;
            Idle.Play();
            GlobalObjects.AddDrawable(Image);
        }

        public void Refresh()
        {
            if (!_isActive)
            {
                _isActive = true;
            }
        }

        public void InitActiveState()
        {
            Idle = new AnimationSprite("trick_idle_up")
            {
                Parent = this,
                Z = -10 + Z,
                Iterations = -1,
                Pivot = new Vector2(0.5f, 0.5f),
                LocalPosition = new Vector2(Width * 0.5f, Height * 0.5f),
            };
            Active = new AnimationSprite("trick_active_up")
            {
                Parent = this,
                Z = -10 + Z,
                Iterations = 1,
                Pivot = new Vector2(0.5f, 0.5f),
                LocalPosition = new Vector2(Width * 0.5f, Height * 0.5f),
            };

            Image = new ImageRendering("TRACK_" + _itemName)
            {
                Parent = this,
                Z = -10 + Z,
                Pivot = new Vector2(0.5f, 0.5f),
                LocalPosition = new Vector2(Width * 0.5f, Height * 0.5f + 23.28f),
                LocalScale = new Vector2(0.9f, 0.9f)
            };

            Image.GenerateContent();

            Idle.Play();
        }

        public void RunActivate()
        {
            GlobalObjects.RemoveDrawable(Image);
            Idle.Finish();
            Active.Play();
        }

        public void ExpandTrickAreaOnce(int sign)
        {
            if (!_wasExpanded)
            {
                if (sign < 0)
                {
                    Point2.X += _originalWidth;
                    Point3.X += _originalWidth;
                }
                else
                {
                    Point1.X -= _originalWidth;
                    Point4.X -= _originalWidth;
                }

                rectangle.Set((float)Point1.X, (float)Point1.Y, (float)(Point2.X - Point1.X), (float)(Point4.Y - Point1.Y));
                _wasExpanded = true;
            }
        }
    }
}
