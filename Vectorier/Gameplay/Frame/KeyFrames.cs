#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vectorier.Gameplay.Frame;

public class KeyFrames
{
    protected List<List<Vector3?>> _Data = new List<List<Vector3?>>();

    protected int _Size;

    protected int _ActiveFrame;

    public int Size => _Size;

    public int SizeMinusActiveFrame => _Size - _ActiveFrame;

    public KeyFrames()
    {
        ResizeData(2, 46);
    }

    public void NextActiveFrame()
    {
        _ActiveFrame++;
    }

    public void InterruptFramesSeted()
    {
        _Size = 2;
    }

    public List<Vector3?> GetFrame(int index)
    {
        if (_Size <= index)
        {
            return null;
        }
        return _Data[index];
    }

    public List<Vector3?> GetActiveFrame(int shift)
    {
        return _Data[_ActiveFrame + shift];
    }

    public void Reset()
    {
        _Size = 0;
        _ActiveFrame = 0;
    }

    public void SetFrames(int from, int to, Vector3[][] data)
    {
        ResizeIfNeed(data[from].Length);
        for (int i = from; i <= to; i++)
        {
            SetFrame(data[i]);
        }
    }

    public void ResizeIfNeed(int size)
    {
        for (int i = 0; i < _Data.Count; i++)
        {
            while (_Data[i].Count < size)
            {
                _Data[i].Add(new Vector3());
            }

            while (_Data[i].Count > size)
            {
                _Data[i].RemoveAt(_Data[i].Count - 1);
            }
        }
    }

    public void SetFrame(Vector3[] p_data)
    {
        _Size++;
        if (_Data.Count < _Size)
        {
            ResizeData(1, p_data.Length);
        }
        var data = _Data[_Size - 1];
        if (data.Count != p_data.Length)
        {
            Console.WriteLine("Input and data size don't match");
            return;
        }
        if (data.Count > 0)
        {
            for (int i = 0; i < data.Count; i++)
            {
                var v = data[i];
                data[i] = v = p_data[i];
            }
        }
    }

    protected void ResizeData(int count, int size)
    {
        for (int i = 0; i < count; i++)
        {
            var list = new List<Vector3?>(size);
            _Data.Add(list);
            if (size > 0)
            {
                for (int j = 0; j < size; j++)
                {
                    list.Add(new Vector3());
                }
            }
        }
    }
}
