// The parts of the game's LevelMainController that the player's movement calls. In the game a death, a loss or an
// arrest restarts the level after a second; in the editor the character stays as it is (physics after a death)
// until the Move Visualizer's Clear, Restart or a new spawn.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay
{
    public sealed class LevelMainController
    {
        public static LevelMainController current { get; set; }

        public LevelMainController(Location.Location location)
        {
            Location = location;
        }

        public Location.Location Location { get; }

        /// <summary>The game's Slow Mode (S): 10 frames per tick instead of 1. Not used by the editor.</summary>
        public float slowModeFrames { get; set; } = 1;

        public static bool IsHunterMode => false;

        public bool IsDeath { get; private set; }

        public bool IsWin { get; private set; }

        public void Reset()
        {
            IsDeath = false;
            IsWin = false;
        }

        public void Win(ModelHuman modelHuman, float time)
        {
            if (IsWin || IsDeath)
            {
                return;
            }
            modelHuman.IsGadget = false;
            IsWin = true;
        }

        public void Murder(ModelHuman modelHuman)
        {
            if (!IsWin && !IsDeath)
            {
                modelHuman.IsGadget = false;
                modelHuman.Death(GameEndType.GE_MURDER);
                IsDeath = true;
            }
        }

        public void Death(ModelHuman modelHuman, float time)
        {
            if (!IsWin && !IsDeath && !modelHuman.IsBot)
            {
                modelHuman.IsGadget = false;
                modelHuman.Death(GameEndType.GE_DEATH);
                modelHuman.StartPhysics();
                IsDeath = true;
            }
        }

        public void Loss(ModelHuman modelHuman, float time)
        {
            if (!IsWin && !IsDeath)
            {
                Camera.LocationCamera.Current?.Stop();
                modelHuman.IsGadget = false;
                modelHuman.Death(GameEndType.GE_LOSS);
                IsDeath = true;
            }
        }
    }
}
