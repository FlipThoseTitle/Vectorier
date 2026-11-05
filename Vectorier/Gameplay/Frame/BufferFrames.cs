// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace Vectorier.Gameplay.Frame
{
    public class BufferFrames : KeyFrames
    {
        public bool IsBufferEmpty => _Size == _ActiveFrame;

        public new void Reset()
        {
            _Size = _ActiveFrame;
        }

        public void InitBuffer(int p_frames)
        {
            _Size = p_frames;
            _ActiveFrame = 0;
            if (_Data.Count < p_frames)
            {
                ResizeData(p_frames - _Data.Count, 46);
            }
        }

        public void VelocityBuffer(Vector3 p_point)
        {
            for (int i = _ActiveFrame; i < _Size; i++)
            {
                List<Vector3?> list = _Data[i];
                for (int j = 0; j < list.Count; j++)
                {
                    list[j] += p_point;
                }
            }
        }
    }
}