#nullable disable warnings
using System;
using System.IO;
using System.Numerics;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.Runners.Animation;

public class AnimationSprite : IDrawable, IUpdatable
{
    public float Z { get; set; }

    public IParent Parent;

    public Vector2 LocalPosition;

    public Vector2 WorldPosition
    {
        get
        {
            if (Parent == null)
            {
                return LocalPosition;
            }
            return Parent.WorldPosition + (LocalPosition * Parent.WorldScale);
        }
    }

    public Vector2 LocalScale = Vector2.One;

    public Vector2 WorldScale
    {
        get
        {
            if (Parent == null)
            {
                return LocalScale;
            }
            return LocalScale * Parent.WorldScale;
        }
    }

    public float Width = -1;

    public float Height = -1;

    public string Name;

    public int TotalFrames;

    public int CurrentFrame;

    public bool IsWork;

    public int Iterations = -1;

    public int IterationsCounter = -1;

    public Action OnIterationsEnd;

    public float Times;

    public float FPS = 20;

    public AtlasDecoder.Atlas Atlas;

    public Matrix? Matrix;

    public Vector2 Pivot = Vector2.Zero;

    public Color Color = Color.White;

    public AnimationSprite(string name, float width = -1, float height = -1)
    {
        Name = name;
        Width = width;
        Height = height;

        var path = Path.Combine(VectorPaths.AnimatedTextures, name);
        if (ResourceManager.FileExists(path, out string atlasPath, ".plist", ".json") && ResourceManager.FileExists(path, out string imagePath, ".png", ".jpg", ".jpeg"))
        {
            Atlas = AtlasDecoder.Decode(atlasPath, imagePath);

            TotalFrames = Atlas.Count;

            return;
        }

        Diagnostics.GameLog.Warning("Loading the level", $"The animated texture \"{name}\" does not exist: it is not drawn.");
    }

    public void Play()
    {
        IsWork = true;
        CurrentFrame = 0;
        Times = 0f;
        IterationsCounter = 0;

        GlobalObjects.AddUpdatable(this);
        GlobalObjects.AddDrawable(this);
    }

    public void Finish()
    {
        IsWork = false;
        OnIterationsEnd?.Invoke();

        GlobalObjects.RemoveUpdatable(this);
        GlobalObjects.RemoveDrawable(this);
    }

    public void SetSpriteAnimation()
    {
        CurrentFrame++;
        if (TotalFrames <= CurrentFrame)
        {
            CurrentFrame = 0;
            IterationsCounter++;
            if (Iterations != -1 && Iterations <= IterationsCounter)
            {
                Finish();
            }
        }
    }

    public void Update()
    {
        if (Parent != null && (Parent is Runner runner && !runner.IsEnabled))
        {
            return;
        }

        if (IsWork)
        {
            Times += VectorGame.TickSeconds; // one logic tick (see VectorGame.TicksThisFrame)
            if (1 / FPS <= Times)
            {
                SetSpriteAnimation();
                Times -= 1 / FPS;
            }
        }
    }

    public void Draw()
    {
        if (Parent != null && (Parent is Runner runner && !runner.IsEnabled))
        {
            return;
        }
        if (!IsWork || Atlas == null || Atlas.Sprites.Count <= 0 || CurrentFrame >= Atlas.Sprites.Count)
        {
            return;
        }

        var color = Color.White;
        if (Parent != null && Parent is VisualRunner image)
        {
            color = image.Color;
        }

        Atlas.Draw(CurrentFrame, WorldPosition, WorldScale, color, Pivot, Width, Height, Matrix);
    }

    public void Draw(int frame)
    {

        if (Atlas == null || Atlas.Sprites.Count <= 0 || CurrentFrame >= Atlas.Sprites.Count)
        {
            return;
        }
        var color = Color;
        if (Parent != null && Parent is VisualRunner image)
        {
            color = image.Color;
        }

        Atlas.Draw(frame, WorldPosition, WorldScale, color, Pivot, Width, Height, Matrix);
    }
}
