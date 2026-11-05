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
using System.Xml;
using Vectorier.Gameplay.Frame;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Animation
{
    public class AnimationInfo
    {
        public const int Jumps = 1;

        public const int Slides = 2;

        private int _Id;

        public List<AnimationInterval> Intervals = new List<AnimationInterval>();

        private readonly int _endFrame;

        private readonly bool _isPart;

        private Provider _frames;

        private bool _Loop;

        private static readonly List<AnimationInterval> TempIntervalList = new List<AnimationInterval>();

        public int Id
        {
            get => _Id;
            set => _Id = value;
        }

        public string Name
        {
            get;
        }

        public bool IsTrick
        {
            get;
            protected set;
        }

        public bool Mirror
        {
            get;
        }

        public int Type
        {
            get;
        }

        public int SubType
        {
            get;
            private set;
        }

        public string FileName
        {
            get;
            private set;
        }

        public int FirstFrame
        {
            get;
        }

        public int EndFrame
        {
            get
            {
                if (_endFrame < _frames.Length)
                {
                    return _endFrame;
                }
                return _frames.Length;
            }
        }

        public int MidFrames
        {
            get;
        }

        public string PivotNode
        {
            get;
        }

        public int Priority
        {
            get;
        }

        public Vector3 Velocity
        {
            get;
        }

        public Vector3 Gravity
        {
            get;
        }

        public bool Binding
        {
            get;
        }

        public float DeltaDetectorV
        {
            get;
        }

        public float DeltaDetectorH
        {
            get;
        }

        public float AutoPositionDetectorV
        {
            get;
        }

        public float AutoPositionDetectorH
        {
            get;
        }

        public float LandingPositionDetectorH
        {
            get;
        }

        public float LandingPositionDetectorV
        {
            get;
        }

        public int PlatformAnticipationFrames
        {
            get;
        }

        public bool Loop
        {
            get => _Loop;
            set => _Loop = value;
        }

        /// <summary>Which physics attributes moves.xml sets on this move (Moves Information, Physics).</summary>
        public bool HasBinding { get; }

        public bool HasGravity { get; }

        public bool HasVelocityX { get; }

        public bool HasVelocityY { get; }

        public AnimationInfo(XmlNode node)
        {
            Velocity = new Vector3();
            Gravity = new Vector3();



            _isPart = node.Attributes["Part"].ParseBool();
            Name = node.Name;
            _Id = node.Attributes["ID"].ParseInt();
            FileName = node.Attributes["FileName"].ParseString();
            FirstFrame = node.Attributes["FirstFrame"].ParseInt();
            MidFrames = node.Attributes["MidFrames"].ParseInt(2);
            _endFrame = node.Attributes["EndFrame"].ParseInt();
            PivotNode = node.Attributes["PivotNode"].Value;
            Type = node.Attributes["Type"].ParseInt(1);
            SubType = node.Attributes["SubType"].ParseInt();
            Priority = node.Attributes["Priority"].ParseInt(1);
            Binding = node.Attributes["Binding"].ParseBool();
            HasBinding = node.Attributes["Binding"] != null;
            HasGravity = node.Attributes["Gravity"] != null;
            HasVelocityX = node.Attributes["VelocityX"] != null;
            HasVelocityY = node.Attributes["VelocityY"] != null;
            DeltaDetectorH = node.Attributes["DeltaDetectorH"].ParseFloat();
            DeltaDetectorV = node.Attributes["DeltaDetectorV"].ParseFloat();
            Mirror = node.Attributes["Mirror"].ParseBool(true);
            var x = node.Attributes["VelocityX"].ParseFloat();
            var y = node.Attributes["VelocityY"].ParseFloat();
            Velocity = new Vector3(x, y, 0);
            Gravity = new Vector3(0, node.Attributes["Gravity"].ParseFloat(), 0);
            AutoPositionDetectorH = node.Attributes["AutoPositionDetectorH"].ParseFloat(-1);
            AutoPositionDetectorV = node.Attributes["AutoPositionDetectorV"].ParseFloat(-1);
            LandingPositionDetectorH = node.Attributes["LandingPositionDetectorH"].ParseFloat(-1);
            LandingPositionDetectorV = node.Attributes["LandingPositionDetectorV"].ParseFloat(-1);
            PlatformAnticipationFrames = (int)node.Attributes["PlatformAnticipationFrames"].ParseFloat();

        }

        public void LoadBin()
        {
            FileName = VectorPaths.AnimationBinary + "/" + FileName;
            if (!_isPart && !IsTrick)
            {
                LoadBinary(true);
            }
        }

        public virtual void LoadBinary(bool useCache)
        {
            _frames = AnimationBinaryParser.ParseFile(FileName, useCache);
        }

        public virtual void UnloadBinary()
        {
            _frames = null;
        }

        public List<AnimationInterval> Interval(int pFrame)
        {
            TempIntervalList.Clear();
            for (int i = 0; i < Intervals.Count; i++)
            {
                var interval = Intervals[i];
                if (pFrame >= interval.BeginFrame && pFrame <= interval.EndFrame)
                {
                    TempIntervalList.Add(interval);
                }
            }
            return TempIntervalList;
        }

        public void CloneFrames(int pStart, int end, KeyFrames frames, int nodesCount)
        {
            int p_to = end <= 0 || end > _frames.Length - 1 ? _frames.Length - 1 : end;
            frames.SetFrames(pStart, p_to, _frames.Data);
        }
    }
}
