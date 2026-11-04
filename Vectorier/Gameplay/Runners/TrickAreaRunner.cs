// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
// The trick's idle / active sprites and its track image are not part of the editor's gameplay: only what the
// character's movement uses is kept.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Runners
{
    public class TrickAreaRunner : AreaRunner
    {
        private readonly float _originalWidth;

        private bool _wasExpanded;

        public static TrickAreaRunner Current;

        private bool _isActive;

        private string _itemName;

        private int _score;

        public bool isActive => _isActive;

        public string itemName => _itemName;

        public int score => _score;

        public TrickAreaRunner(float x, float y, float width, float height, string typeName, string name, string itemName, int score)
            : base(AreaType.Trick, x, y, width, height, typeName, name)
        {
            _itemName = itemName;
            _score = score;
            _originalWidth = width;
            _isActive = true;
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
            model.CollectTrick(Current);
        }

        public override void Reset()
        {
            base.Reset();
            Current = null;
            _isActive = true;
            _wasExpanded = false;
        }

        public void Refresh()
        {
            _isActive = true;
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
