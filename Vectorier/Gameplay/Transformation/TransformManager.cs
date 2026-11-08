#nullable disable warnings
using System.Collections.Generic;
namespace Vectorier.Gameplay.Transformation;

public class TransformManager
{
    private List<TransformSystem> _storage = new List<TransformSystem>();

    private List<TransformSystem> _flushStorage = new List<TransformSystem>();

    public void Clear()
    {
        foreach (var t in _storage)
        {
            t.Reset();
        }
        _storage.Clear();
        _flushStorage.Clear();
    }

    public void Add(TransformSystem system)
    {
        if (!_storage.Contains(system))
        {
            foreach (var t in system.Storage)
            {
                if (t.Type == Type.Move)
                {
                    t.Runner.AddMoveSystem((MoveTransform)t);
                    t.Runner.TransformationStart();
                }
                if (t.Type == Type.Scale)
                {
                    t.Runner.TransformationStart();
                }
            }
            _storage.Add(system);
        }

    }

    public void Remove(TransformSystem system)
    {
        foreach (var t in system.Storage)
        {
            if (t.Type == Type.Move)
            {
                t.Runner.RemoveMoveSystem((MoveTransform)t);
            }
        }
        _flushStorage.Add(system);
    }

    public List<TransformSystem> Find(string name)
    {
        List<TransformSystem> list = new List<TransformSystem>();
        for (int i = 0; i < _storage.Count; i++)
        {
            var t = _storage[i];
            if (t.Name == name)
            {
                list.Add(t);
            }
        }
        return list;
    }

    public void Update()
    {
        for (int i = 0; i < _storage.Count; i++)
        {
            if (!_storage[i].Update())
            {
                Remove(_storage[i]);
            }
        }
        if (_flushStorage.Count == 0)
        {
            return;
        }
        for (int i = 0; i < _flushStorage.Count; i++)
        {
            _flushStorage[i].Parent.TransformationEnd();
            _storage.Remove(_flushStorage[i]);
        }
        _flushStorage.Clear();
    }
}
