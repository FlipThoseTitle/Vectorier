#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Result;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers;

public class ControllerPlatforms
{
    private ModelHuman _model;

    private DetectorLine _detectorH;

    private DetectorLine _detectorV;
    private List<QuadRunner> _vertical = new List<QuadRunner>();
    private Affiliation cachedAffiliationResult = new Affiliation();

    public List<Belong> Belongs { get; } = new List<Belong>();

    public List<QuadRunner> Horizontal { get; } = new List<QuadRunner>();

    public ControllerPlatforms(ModelHuman modelHuman)
    {
        _model = modelHuman;
        _detectorH = _model.ModelObject.DetectorHorizontalLine;
        _detectorV = _model.ModelObject.DetectorVerticalLine;
    }

    public void Clear(List<QuadRunner> quads)
    {
        foreach (var quad in quads)
        {
            var indexV = Index(quad, _detectorV.Type);
            var indexH = Index(quad, _detectorH.Type);
            if (indexV > -1)
            {
                Remove(indexV, _detectorV.Type);
            }
            if (indexH > -1)
            {
                Remove(indexH, _detectorH.Type);
            }
        }
    }

    public void Render(QuadRunner platform)
    {
        platform.SetTempPoint(_model.PlatformAnticipationFrames);
        Render(platform, _detectorH);
        Render(platform, _detectorV);
    }

    public void Render(QuadRunner platform, DetectorLine detector)
    {
        platform.DetectorAffiliation(detector, cachedAffiliationResult);
        int index = Index(platform, detector.Type);
        if (cachedAffiliationResult.Type > 0 && index == -1)
        {
            int p_side = platform.Side(detector, cachedAffiliationResult, _model.Sign);
            cachedAffiliationResult.Clear();
            switch (detector.Type)
            {
                case DetectorLine.DetectorType.Horizontal:
                    Horizontal.Add(platform);
                    break;
                case DetectorLine.DetectorType.Vertical:
                    _vertical.Add(platform);
                    break;
            }
            Belongs.Add(new Belong(platform, detector));
            Update(detector);
            Input(platform, detector, p_side);
            return;
        }
        if (index > -1 && cachedAffiliationResult.CrossList3.Count == 0 && !cachedAffiliationResult.Hits)
        {
            index = Remove(index, detector.Type);
            for (int num = Belongs.Count - 1; num >= 0; num--)
            {
                if (Belongs[num].Platform == platform && Belongs[num].Detector == detector)
                {
                    Belongs.RemoveAt(num);
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
                list = Horizontal;
                break;
            case DetectorLine.DetectorType.Vertical:
                list = _vertical;
                break;
        }
        if (list.Count > 0)
        {
            detector.Node.Data = list[list.Count - 1];
        }
    }

    public void Input(QuadRunner platform, DetectorLine detector, int side)
    {
        _model.ControllerAnimations.CheckDetectors(new DetectorEvent(detector, DetectorEvent.DetectorEventType.On, side));
    }

    public void Output(QuadRunner platform, DetectorLine detector)
    {
        _model.ControllerAnimations.CheckDetectors(new DetectorEvent(detector, DetectorEvent.DetectorEventType.Off, -1));
        detector.Node.Data = null;
    }

    public int Index(QuadRunner platform, DetectorLine.DetectorType type)
    {
        switch (type)
        {
            case DetectorLine.DetectorType.Horizontal:
                {
                    for (int j = 0; j < Horizontal.Count; j++)
                    {
                        if (platform == Horizontal[j])
                        {
                            return j;
                        }
                    }
                    break;
                }
            case DetectorLine.DetectorType.Vertical:
                {
                    for (int i = 0; i < _vertical.Count; i++)
                    {
                        if (platform == _vertical[i])
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
                _vertical.RemoveAt(index);
                return _vertical.Count;
            case DetectorLine.DetectorType.Horizontal:
                Horizontal.RemoveAt(index);
                return Horizontal.Count;
            default:
                return 0;
        }
    }

    public void Reset()
    {
        Horizontal.Clear();
        _vertical.Clear();
        _detectorH.Node.Data = null;
        _detectorV.Node.Data = null;
    }
}
