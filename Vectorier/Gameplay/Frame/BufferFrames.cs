#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;

namespace Vectorier.Gameplay.Frame;

public class BufferFrames : KeyFrames
{
    public bool IsBufferEmpty => _Size == _ActiveFrame;

    public new void Reset()
    {
        _Size = _ActiveFrame;
    }

    public void InitBuffer(int frames)
    {
        _Size = frames;
        _ActiveFrame = 0;
        if (_Data.Count < frames)
        {
            ResizeData(frames - _Data.Count, 46);
        }
    }

    public void VelocityBuffer(Vector3 point)
    {
        for (int i = _ActiveFrame; i < _Size; i++)
        {
            List<Vector3?> list = _Data[i];
            for (int j = 0; j < list.Count; j++)
            {
                list[j] += point;
            }
        }
    }
}
