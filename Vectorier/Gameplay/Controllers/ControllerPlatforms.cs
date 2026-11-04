// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Result;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerPlatforms
    {
        private ModelHuman _Model;

        private DetectorLine _DetectorH;

        private DetectorLine _DetectorV;

        private List<Belong> _Belongs = new List<Belong>();

        private List<QuadRunner> _Vertical = new List<QuadRunner>();

        private List<QuadRunner> _Horizontal = new List<QuadRunner>();

        private Affiliation cachedAffiliationResult = new Affiliation();

        public List<Belong> Belongs => _Belongs;

        public List<QuadRunner> Vertical => _Vertical;

        public List<QuadRunner> Horizontal => _Horizontal;

        public ControllerPlatforms(ModelHuman modelHuman)
        {
            _Model = modelHuman;
            _DetectorH = _Model.ModelObject.DetectorHorizontalLine;
            _DetectorV = _Model.ModelObject.DetectorVerticalLine;
        }

        public void Clear(List<QuadRunner> quads)
        {
            foreach (var quad in quads)
            {
                var indexV = Index(quad, _DetectorV.Type);
                var indexH = Index(quad, _DetectorH.Type);
                if (indexV > -1)
                {
                    Remove(indexV, _DetectorV.Type);
                }
                if (indexH > -1)
                {
                    Remove(indexH, _DetectorH.Type);
                }
            }
        }

        public void Render(QuadRunner platform)
        {
            platform.SetTempPoint(_Model.PlatformAnticipationFrames);
            Render(platform, _DetectorH);
            Render(platform, _DetectorV);
        }

        public void Render(QuadRunner platform, DetectorLine detector)
        {
            platform.DetectorAffiliation(detector, cachedAffiliationResult);
            int index = Index(platform, detector.Type);
            if (cachedAffiliationResult.Type > 0 && index == -1)
            {
                int p_side = platform.Side(detector, cachedAffiliationResult, _Model.Sign);
                cachedAffiliationResult.Clear();
                switch (detector.Type)
                {
                    case DetectorLine.DetectorType.Horizontal:
                        _Horizontal.Add(platform);
                        break;
                    case DetectorLine.DetectorType.Vertical:
                        _Vertical.Add(platform);
                        break;
                }
                _Belongs.Add(new Belong(platform, detector));
                Update(detector);
                Input(platform, detector, p_side);
                return;
            }
            if (index > -1 && cachedAffiliationResult.CrossList3.Count == 0 && !cachedAffiliationResult.Hits)
            {
                index = Remove(index, detector.Type);
                for (int num = _Belongs.Count - 1; num >= 0; num--)
                {
                    if (_Belongs[num].Platform == platform && _Belongs[num].Detector == detector)
                    {
                        _Belongs.RemoveAt(num);
                    }
                }
                Update(detector);
                if (index == 0)
                {
                    Output(platform, detector);
                }
            }
        }

        public void Update(DetectorLine detector)
        {
            List<QuadRunner> list = null;
            switch (detector.Type)
            {
                default:
                    return;
                case DetectorLine.DetectorType.Horizontal:
                    list = _Horizontal;
                    break;
                case DetectorLine.DetectorType.Vertical:
                    list = _Vertical;
                    break;
            }
            if (list.Count > 0)
            {
                detector.Node.Data = list[list.Count - 1];
            }
        }

        public void Input(QuadRunner platform, DetectorLine detector, int side)
        {
            _Model.ControllerAnimations.CheckDetectors(new DetectorEvent(detector, DetectorEvent.DetectorEventType.On, side));
        }

        public void Output(QuadRunner platform, DetectorLine detector)
        {
            _Model.ControllerAnimations.CheckDetectors(new DetectorEvent(detector, DetectorEvent.DetectorEventType.Off, -1));
            detector.Node.Data = null;
        }

        public int Index(QuadRunner platform, DetectorLine.DetectorType type)
        {
            switch (type)
            {
                case DetectorLine.DetectorType.Horizontal:
                    {
                        for (int j = 0; j < _Horizontal.Count; j++)
                        {
                            if (platform == _Horizontal[j])
                            {
                                return j;
                            }
                        }
                        break;
                    }
                case DetectorLine.DetectorType.Vertical:
                    {
                        for (int i = 0; i < _Vertical.Count; i++)
                        {
                            if (platform == _Vertical[i])
                            {
                                return i;
                            }
                        }
                        break;
                    }
                default:
                    return -1;
            }
            return -1;
        }

        public int Remove(int index, DetectorLine.DetectorType type)
        {
            switch (type)
            {
                case DetectorLine.DetectorType.Vertical:
                    _Vertical.RemoveAt(index);
                    return _Vertical.Count;
                case DetectorLine.DetectorType.Horizontal:
                    _Horizontal.RemoveAt(index);
                    return _Horizontal.Count;
                default:
                    return 0;
            }
        }

        public List<QuadRunner> ActivePlatform(DetectorLine.DetectorType Type)
        {
            if (Type == DetectorLine.DetectorType.Horizontal)
            {
                return _Horizontal;
            }

            if (Type == DetectorLine.DetectorType.Vertical)
            {
                return _Vertical;
            }
            return new List<QuadRunner>();
        }

        public void Reset()
        {
            _Horizontal.Clear();
            _Vertical.Clear();
            _DetectorH.Node.Data = null;
            _DetectorV.Node.Data = null;
        }
    }
}
