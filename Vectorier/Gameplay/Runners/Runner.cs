#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using System.Xml;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Transformation;

namespace Vectorier.Gameplay.Runners;

public abstract class Runner : IParent, IDrawable
{
    public string Name;

    public int Hash;

    public float Z { get; set; }


    public IParent Parent { get; set; }

    public Vector2 LocalPosition { get; set; } = Vector2.Zero;


    public Vector2 WorldPosition
    {
        get
        {
            if (Parent == null)
                return LocalPosition;

            return Parent.TransformPoint(LocalPosition);
        }
    }

    public Vector2 LocalScale { get; set; } = Vector2.One;

    public Vector2 WorldScale
    {
        get
        {
            if (Parent == null)
                return LocalScale;
            return Parent.LocalScale * LocalScale;
        }
    }

    public Vector2 DefaultPosition = Vector2.Zero;

    public Vector2 DefaultScale = Vector2.One;

    public Vector2 TweenPosition;

    public bool IsEnabled = true;

    public RunnerType TypeClass;

    public Elements ParentElements;

    public int ActiveTransformation;

    public List<TransformSystem> TransformationData = new List<TransformSystem>();

    public List<MoveTransform> ActiveMoveSystem;


    public Runner(float x, float y)
    {
        LocalPosition = new Vector2(x, y);
        DefaultPosition = new Vector2(x, y);
    }

    public virtual void InitRunner(Vector2 point)
    {
        Move(point);

        DefaultPosition = LocalPosition;
        DefaultScale = LocalScale;
    }

    public virtual void Move(Vector2 point)
    {
        LocalPosition += point;
    }

    public virtual void UpdatePosition(Vector2 point)
    {
        TweenPosition = point;
        Move(point);
    }

    public virtual void ResetPosition()
    {
        TweenPosition = Vector2.Zero; ;
        foreach (var transform in TransformationData)
        {
            transform.Reset();
        }
        if (ActiveMoveSystem != null)
            ActiveMoveSystem.Clear();
    }

    public virtual void Reset()
    {
        ResetPosition();
        LocalPosition = DefaultPosition;
        LocalScale = DefaultScale;
        ActiveTransformation = 0;
        if (ActiveMoveSystem != null)
            ActiveMoveSystem.Clear();
    }

    public virtual bool Render()
    {
        return true;
    }

    public virtual void Draw()
    {
    }

    public void SetXmlList(XmlNode node)
    {
        if (node != null)
        {
            ParseTransformation(node["Dynamic"]);
        }
    }

    public void ParseTransformation(XmlNode node)
    {
        foreach (XmlNode childNode in node.ChildNodes)
        {
            if (childNode.Name == "Transformation")
            {
                var transform = TransformSystem.Create(childNode);
                if (transform != null)
                {
                    transform.Parent = this;
                    AddTransformationData(transform);
                }
            }
        }
    }

    public void AddMoveSystem(MoveTransform system)
    {
        if (ActiveMoveSystem == null)
            ActiveMoveSystem = new List<MoveTransform>();

        ActiveMoveSystem.Add(system);
    }

    public void RemoveMoveSystem(MoveTransform system)
    {
        if (ActiveMoveSystem != null)
        {
            ActiveMoveSystem.Remove(system);
        }
    }

    public virtual void TransformationStart()
    {
        ActiveTransformation++;
    }

    public virtual void TransformationEnd()
    {
        ActiveTransformation--;
    }

    private void AddTransformationData(TransformSystem data)
    {
        if (TransformationData == null)
            TransformationData = new List<TransformSystem>();
        TransformationData.Add(data);
    }

    public virtual void TransformColor(Color color)
    {

    }

    public virtual void TransformColorEnd(Color color)
    {

    }

    public virtual void TransformResize(Vector2 size)
    {

    }

    public virtual void TransformRotate(float angle)
    {
    }

    public Vector2 TransformPoint(Vector2 point)
    {
        return WorldPosition + point * WorldScale;
    }

    public Vector2 TransformPoint(Vector3 point)
    {
        return TransformPoint(new Vector2(point.X, point.Y));
    }
}
