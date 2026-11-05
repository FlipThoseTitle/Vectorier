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

namespace Vectorier.Gameplay.Runners.Animation
{
    //FIX
    public class AnimationRendering : Runner
    {
        private int _CurrentFrame;

        private bool _Replay;

        protected bool _IsPlay;

        private int _CountHalf;

        private int _TotalHalf;

        public string Path = "";

        protected float _Width;

        protected float _Height;

        protected int _TotalFrames;

        protected List<Texture2D> frames = new List<Texture2D>();

        public AnimationSprite Animator;

        public AnimationRendering(string name, float X = 0f, float Y = 0f, float width = 100f, float height = 100f, bool replay = false, int speed = 1)
            : base(X, Y)
        {
            Name = name;
            _Width = width;
            _Height = height;
            _Replay = replay;
            _TotalHalf = speed;

        }

        public virtual void Init(float pivotX = 0, float pivotY = 0)
        {
            Animator = new AnimationSprite(Name)
            {
                Parent = this,
                Pivot = new System.Numerics.Vector2(pivotX, pivotY),
                Z = Z
            };
            _TotalFrames = Animator.TotalFrames;
            IsEnabled = false;
        }

        public override bool Render()
        {
            if (_IsPlay)
            {
                if (_CountHalf < _TotalHalf)
                {
                    _CountHalf++;
                    return false;
                }
                _CountHalf = 0;
                _CurrentFrame++;
                if (_CurrentFrame < _TotalFrames)
                {
                    return false;
                }
                if (_Replay)
                {
                    _CurrentFrame = 0;
                    return false;
                }
                Stop();
            }
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            _CurrentFrame = 0;
            _CountHalf = 0;
            _IsPlay = false;
            //Animator.Draw(_CurrentFrame);
        }

        public virtual void Stop(bool value = false)
        {
            _IsPlay = false;
            GlobalObjects.RemoveDrawable(this);
            if (value)
            {
                IsEnabled = false;
            }
        }

        public override void Draw()
        {
            if (!_IsPlay || _CurrentFrame >= _TotalFrames)
            {
                return;
            }
            Animator.Draw(_CurrentFrame);
        }

        public void PlayFrom(int value)
        {
            _CurrentFrame = value;
            _CountHalf = _TotalHalf;
            GlobalObjects.AddDrawable(this);
            Render();
            _IsPlay = true;

        }
    }
}
