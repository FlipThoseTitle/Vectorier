#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Result;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Models;

public class Model : IDrawable
{
    private bool _isEnabled;
    protected ModelObject _ModelObject;

    protected ControllerPhysics _ControllerPhysics;

    protected ControllerCollisions _ControllerCollisions;

    protected ControllerStrike _controllerStrike;

    protected ModelType _Type;


    public virtual bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public bool IsVisible
    {
        get => _ModelObject.IsVisible;
        set => _ModelObject.IsVisible = value;
    }

    public string Name
    {
        get;
        set
        {
            field = value;
            _ModelObject.Name = value;
        }
    }

    public ModelObject ModelObject => _ModelObject;

    public ControllerPhysics ControllerPhysics => _ControllerPhysics;

    public ControllerCollisions ControllerCollisions => _ControllerCollisions;

    public float Z { get; set; } = -100;

    public ModelType Type
    {
        get => _Type;
        set => _Type = value;
    }

    public Color Color
    {
        get => _ModelObject.Color;
        set => _ModelObject.Color = value;
    }

    public virtual Rectangle Rectangle => _ModelObject.Rectangle == null ? new Rectangle() : _ModelObject.Rectangle;

    public bool IsPhysics => _ControllerPhysics.IsPhysics;

    public static List<Model> ModelsAll = new List<Model>();

    public Model(List<string> skins, ModelType type)
    {
        _Type = type;
        _ModelObject = new ModelObject(skins);
        _ModelObject.IsAuxiliary = false;
        _ModelObject.Parent = this;

        ModelsAll.Add(this);

    }

    public virtual void Draw()
    {
        if (VectorGame.RenderEdges)
        {
            for (int i = 0; i < _ModelObject.EdgesAll.Count; i++)
            {
                _ModelObject.EdgesAll[i].Draw();
            }
        }
        if (VectorGame.RenderDetectors)
        {
            if (_ModelObject.DetectorHorizontalLine != null)
                _ModelObject.DetectorHorizontalLine.Draw();

            if (_ModelObject.DetectorVerticalLine != null)
                _ModelObject.DetectorVerticalLine.Draw();
        }
        if (VectorGame.NodeMode != VectorGame.NodeDisplay.None)
        {
            var nodes = _ModelObject.NodesAll;
            // Ordinary nodes first (All mode only), then COM / detectors / Camera on top so they stay visible.
            if (VectorGame.NodeMode == VectorGame.NodeDisplay.All)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (!nodes[i].IsImportant)
                        nodes[i].Draw();
                }
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].GetImportantStyle(out float radius, out Color color))
                    nodes[i].Draw(radius, color);
            }
        }
        if (VectorGame.RenderBoxes)
        {
            var vs = Sets.Current.Containers[1];
            Raylib.DrawRectangleLinesEx(new Vectorier.Gameplay.Rl.Rectangle(vs.TransformPoint(Rectangle.Origin), Rectangle.Size * vs.FrameScale), 2, Color.Red);
        }

    }

    public virtual void Init()
    {
        _ControllerPhysics = new ControllerPhysics(_ModelObject);
        _ControllerCollisions = new ControllerCollisions(this);
        _controllerStrike = new ControllerStrike(this);
        _ControllerPhysics.Stop();
    }

    public virtual void StartPhysics()
    {
        _ControllerPhysics.Start();
    }

    public virtual void Strike(ModelLine edge, Vector3 point, Vector3 impulse)
    {
        _controllerStrike.Striking(edge, point, impulse);
    }

    public virtual void OnCollisionPlatform(ModelEvent<Collision> Event)
    {
    }

    public virtual void OnCollisionModel(Collision collision)
    {
    }

    public virtual void Render(List<QuadRunner> platforms = null)
    {
    }

    public virtual void Reset()
    {
        _ModelObject.Reset();
    }

    public void Position(Vector3 vector, string Name = "NPivot")
    {
        _ModelObject.Position(vector, Name);
    }

    public Vector3 Position(string name = "NPivot", bool isCurrent = true)
    {
        return _ModelObject != null ? _ModelObject.Position(name, isCurrent) : default;
    }

    public ModelNode GetNode(string nodeName = "NPivot")
    {
        return _ModelObject.GetNode(nodeName);
    }

}
