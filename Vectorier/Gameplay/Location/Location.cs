// Ported from Vector-Raylib-For-Vectorier (Core/Location/Location.cs).
// The game builds its level from the level XML; in the editor the Platforms, Trapezoids and Areas come from the
// open level (GameplayWorld builds them). Only the parts of a tick that move the player are kept: no triggers,
// items, primitives, transformations or other models.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System.Collections.Generic;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Location
{
    public class Location
    {
        private readonly Grid.Grid _Grid = new Grid.Grid();

        private readonly List<QuadRunner> _culledQuads = new List<QuadRunner>();

        private readonly List<QuadRunner> belongQuads = new List<QuadRunner>();

        /// <summary>Platforms and Trapezoids (what the model stands on and collides with).</summary>
        public List<QuadRunner> Quads { get; } = new List<QuadRunner>();

        /// <summary>Platforms, Trapezoids and Areas (what the grid holds).</summary>
        public List<QuadRunner> QuadsAll { get; } = new List<QuadRunner>();

        public List<AreaRunner> Areas { get; } = new List<AreaRunner>();

        public List<ModelHuman> Models { get; } = new List<ModelHuman>();

        public Grid.Grid Grid => _Grid;

        public Location(IEnumerable<QuadRunner> quads, IEnumerable<AreaRunner> areas)
        {
            Quads.AddRange(quads);
            QuadsAll.AddRange(Quads);
            Areas.AddRange(areas);
            QuadsAll.AddRange(Areas);
            _Grid.InitGrid(QuadsAll);
        }

        /// <summary>One game tick (1/60 s): each model moves, then collides with what is around it.</summary>
        public void Render()
        {
            for (int i = 0; i < Models.Count; i++)
            {
                Models[i].ControllerAnimations.VelocityQuads();
                Models[i].Render();
                Collision(Models[i]);
            }
        }

        public void Collision(ModelHuman p_modelHuman)
        {
            p_modelHuman.fixRectangle = true;
            _culledQuads.Clear();
            _Grid.Collect(p_modelHuman.Rectangle, _culledQuads);
            belongQuads.Clear();
            var belongs = p_modelHuman.ControllerCollisions.ControllerPlatforms.Belongs;
            for (int i = belongs.Count - 2; i > 0; i--)
            {
                belongQuads.Add(belongs[i].Platform);
            }
            foreach (var quad in belongQuads)
            {
                _culledQuads.Remove(quad);
                p_modelHuman.ControllerCollisions.UpdateQuad(quad);
            }
            foreach (var quad1 in _culledQuads)
            {
                p_modelHuman.ControllerCollisions.UpdateQuad(quad1, p_modelHuman.Sign);
            }
            foreach (var model in Models)
            {
                p_modelHuman.ControllerCollisions.CheckModel(model);
            }
            p_modelHuman.fixRectangle = false;
        }

        public void Reload()
        {
            foreach (var quad in QuadsAll)
            {
                quad.Reset();
            }
            _Grid.Reset();
        }
    }
}
