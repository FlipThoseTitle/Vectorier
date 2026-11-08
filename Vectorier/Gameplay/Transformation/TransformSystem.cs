#nullable disable warnings
using System.Collections.Generic;
using System.Xml;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Transformation;

public class TransformSystem
{
    private bool _isPaused;
    private bool _firstIteration = true;

    public List<TransformPrototype> Storage { get; } = new List<TransformPrototype>();

    public Runner Parent
    {
        get; set
        {
            field = value;
            foreach (var t in Storage)
            {
                t.Runner = field;
            }
        }
    }

    public ObjectRunner MainParent { get; set; }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            if (MainParent == null)
            {
                return;
            }
            var list = LevelMainController.current.Location.transformManager.Find(Name);
            for (int i = 0; i < list.Count; i++)
            {
                list[i]._isPaused = _isPaused;
            }
            list.Clear();
        }
    }

    public string Name { get; set; }

    public bool IsSystemTemplate { get; set; }

    public int FramesCount
    {
        get
        {
            int frames = 0;
            foreach (TransformPrototype p in Storage)
            {
                frames += p.Frames;
            }
            return frames;
        }
    }

    public static TransformSystem Create(XmlNode node)
    {
        if (node == null)
        {
            return null;
        }
        var t = new TransformSystem();
        if (t.Parse(node))
        {
            return t;
        }
        return null;
    }

    private bool Parse(XmlNode node)
    {
        Name = node.Attributes["Name"].Value;
        foreach (XmlNode transformNode in node.ChildNodes)
        {
            switch (transformNode.Name)
            {
                case "Move":
                    var move = MoveTransform.Create(transformNode);
                    if (move != null)
                        Storage.Add(move);
                    break;
                case "Color":
                    var color = ColorTransform.Create(transformNode);
                    if (color != null)
                        Storage.Add(color);
                    break;
                case "Size":
                    var size = SizeTransform.Create(transformNode);
                    if (size != null)
                        Storage.Add(size);
                    break;
                case "Rotation":
                    var rotate = RotateTransform.Create(transformNode);
                    if (rotate != null)
                        Storage.Add(rotate);
                    break;
            }
        }
        return true;
    }

    public void Reset()
    {
        _firstIteration = true;
        _isPaused = true;
        foreach (var t in Storage)
        {
            t.Reset();
        }
    }

    public bool Update()
    {
        if (!_isPaused)
        {
            int num = 0;
            foreach (var t in Storage)
            {
                if (_firstIteration)
                {
                    t.CalcDelta();
                }
                if (!t.Iteration())
                {
                    num++;
                }
            }
            if (_firstIteration)
            {
                _firstIteration = false;
            }
            return Storage.Count != 0 && num != Storage.Count;
        }
        return true;

    }

    public int Run()
    {
        IsPaused = false;
        if (MainParent == null)
        {
            LevelMainController.current.Location.transformManager.Add(this);
        }
        else
        {
            RunTransformOnObjectRunner(MainParent);
        }
        return FramesCount;
    }

    public void RunTransformOnObjectRunner(ObjectRunner @object)
    {

        foreach (var runner in @object.Element.Runners)
        {
            var t = Clone();
            t.Parent = runner;
            t.IsSystemTemplate = true;
            LevelMainController.current.Location.transformManager.Add(t);
        }
        foreach (var child in @object.Childs)
        {
            RunTransformOnObjectRunner(child);
        }
    }

    public TransformSystem Clone()
    {
        var t = new TransformSystem();
        t.Name = Name;
        t.IsPaused = IsPaused;
        foreach (var transform in Storage)
        {
            t.Storage.Add(transform.Clone());
        }
        return t;
    }
}
