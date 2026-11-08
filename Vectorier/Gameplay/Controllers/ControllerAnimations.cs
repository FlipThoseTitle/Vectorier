#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Animation.Events;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Frame;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Result;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers;

public class ControllerAnimations
{
    private Model _model;

    private ModelObject _modelObject;

    private AnimationInfo _animationOld;

    private KeyFrames _frames = new KeyFrames();

    private BufferFrames _buffer = new BufferFrames();

    private int _firstFrame;

    private int _endFrame;

    private int _currentNode;

    private int _pointFrame;

    private int _animationFrame;

    private int _oldFrame;

    private List<Vector3> _velocityOld = new List<Vector3>();

    private List<Vector3> _targetPointOld = new List<Vector3>();

    private string _pivotNode;

    private bool _isRender;

    private bool _isMirror;

    private bool _isNewFrame;

    private List<string> _nodesNewPosition;

    private Vector3? _speedTween = null;

    private Vector3 _speedTweenOld = new Vector3();
    private List<List<AnimationReaction>> _forSort = new List<List<AnimationReaction>>();

    public AnimationInfo Animation { get; private set; }

    public int Sign { get; private set; }

    public string Name { get; private set; }

    public bool IsPlay { get; private set; }

    private List<AnimationInterval> Intervals => Animation.Interval(CurrentFrame);

    /// <summary>A copy of the intervals active at the current frame (the debug overlay's view; I key).</summary>
    public List<AnimationInterval> CurrentIntervals => Animation == null ? new List<AnimationInterval>() : new List<AnimationInterval>(Intervals);

    private bool IsBuffer => _buffer != null && !_buffer.IsBufferEmpty;

    public int CurrentFrame
    {
        get
        {
            if (_animationFrame < 3)
            {
                return _firstFrame;
            }
            return _firstFrame + _animationFrame - 2;
        }
    }

    /// <summary>
    /// Safe: written on the playing reaction, it overrides the intervals' Safe; otherwise any active interval
    /// with Safe="1" makes it safe.
    /// </summary>
    public bool SafeInterval
    {
        get
        {
            if (PlayingReaction?.SafeAttribute is bool reactionSafe)
            {
                return reactionSafe;
            }
            return IntervalSafe;
        }
    }

    /// <summary>Safe="1" on an active interval.</summary>
    public bool IntervalSafe
    {
        get
        {
            AnimationInterval animationInterval = null;
            for (int i = 0; i < Intervals.Count; i++)
            {
                animationInterval = Intervals[i];
                if (animationInterval.IsSafe)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// The reaction whose move is playing (null when the move was started another way, e.g. by a trigger).
    /// </summary>
    public AnimationReaction PlayingReaction { get => field != null && Animation != null && field.Name == Animation.Name ? field : null; private set; }

    /// <summary>SafeH / SafeV of the playing reaction when it writes them, else of an active interval (null: neither).</summary>
    public bool? SafeHSetting => PlayingReaction?.SafeHAttribute ?? IntervalSafeSide(true);

    public bool? SafeVSetting => PlayingReaction?.SafeVAttribute ?? IntervalSafeSide(false);

    private bool? IntervalSafeSide(bool horizontal)
    {
        if (Animation == null)
        {
            return null;
        }
        bool? result = null;
        foreach (var interval in Intervals)
        {
            var value = horizontal ? interval.SafeH : interval.SafeV;
            if (value == true)
            {
                return true;
            }
            result ??= value;
        }
        return result;
    }

    /// <summary>Effective SafeH / SafeV: the reaction's or interval's setting, else the detector's (set by the last reaction).</summary>
    public bool IsSafeH => SafeHSetting ?? (_modelObject.DetectorHorizontalLine?.Safe ?? false);

    public bool IsSafeV => SafeVSetting ?? (_modelObject.DetectorVerticalLine?.Safe ?? false);

    /// <summary>The platform the model is bound to (it carries the model when it moves); null: none.</summary>
    public QuadRunner BoundPlatform { get; private set; }

    /// <summary>NoPlatformBoundX / Y of the active intervals (null: none).</summary>
    public Vector2? NoPlatformBound => Animation == null ? null : PlatformBound;

    /// <summary>
    /// Where the COM is expected to go during the playing move: its position in each remaining animation frame,
    /// with the move's VelocityX / VelocityY / Gravity (Binding) applied, as computed when the move started.
    /// </summary>
    public List<Vector3> PredictedPath { get; } = new List<Vector3>();

    public bool LockInterval
    {
        get
        {
            AnimationInterval animationInterval = null;
            for (int i = 0; i < Intervals.Count; i++)
            {
                animationInterval = Intervals[i];
                if (animationInterval.IsLock)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public float AutoPositionDetectorH => Animation.AutoPositionDetectorH;

    public float AutoPositionDetectorV => Animation.AutoPositionDetectorV;

    public float LandingPositionDetectorH => Animation.LandingPositionDetectorH;

    public float LandingPositionDetectorV => Animation.LandingPositionDetectorV;

    public Rectangle BoundingBox
    {
        get
        {
            if (Animation == null)
            {
                return _modelObject.Rectangle;
            }
            AnimationInterval animationInterval = null;
            for (int i = 0; i < Intervals.Count; i++)
            {
                animationInterval = Intervals[i];
                if (!(animationInterval.BoundingBoxLeft == null))
                {
                    return Sign != 1 ? animationInterval.BoundingBoxLeft : animationInterval.BoundingBoxRight;
                }
            }
            return _modelObject.Rectangle;
        }
    }

    public bool IsActionInterval
    {
        get
        {
            AnimationInterval animationInterval = null;
            for (int i = 0; i < Intervals.Count; i++)
            {
                animationInterval = Intervals[i];
                if (animationInterval.IsAction)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private bool IsDelayReaction => _model is ModelHuman && ((ModelHuman)_model).DelayReaction != null;

    private Vector2? PlatformBound
    {
        get
        {
            AnimationInterval animationInterval = null;
            for (int i = 0; i < Intervals.Count; i++)
            {
                animationInterval = Intervals[i];
                if (animationInterval.NoPlatformBound.X != 0f || animationInterval.NoPlatformBound.Y != 0f)
                {
                    return animationInterval.NoPlatformBound;
                }
            }
            return null;
        }
    }

    public float VelocityScale
    {
        get
        {
            int normalPointFrames = (int)Math.Max(Animation.MidFrames, 1);
            return (_pointFrame + 1f) / (normalPointFrames + 1f);
        }
    }

    public ControllerAnimations(Model model)
    {
        Sign = 1;
        _model = model;
        _modelObject = model.ModelObject;
    }

    private void Init(AnimationInfo info, bool reverse, int firstFrame)
    {
        _buffer.Reset();
        _frames.Reset();
        Animation = info;
        Name = info.Name;
        _firstFrame = firstFrame >= 0 ? firstFrame : info.FirstFrame;
        if (reverse)
        {
            Sign *= -1;
        }
        _frames.InterruptFramesSeted();
        _endFrame = info.EndFrame;
        Animation.CloneFrames(_firstFrame, _endFrame, _frames, 0);
        SetInterruptFrames();
        _pivotNode = info.PivotNode;
        _currentNode = _modelObject.GetNodeIdByName(_pivotNode);
        ShiftPoint();
        VelocityPoint();
        MirrorNode("NHeel_1", "NHeel_2");
        SetNodesNewPosition();
        RecordPredictedPath();
        if (_modelObject.DetectorVerticalLine != null)
        {
            _modelObject.DetectorVerticalLine.Delta(info.DeltaDetectorV);
        }
        if (_modelObject.DetectorHorizontalLine != null)
        {
            _modelObject.DetectorHorizontalLine.Delta(info.DeltaDetectorH);
        }
        _animationOld = Animation;
        IsPlay = true;
        _isRender = true;
        _isNewFrame = false;
        _animationFrame = 0;
        _oldFrame = 0;
    }

    public void Render()
    {
        if (!IsPlay && !_isRender)
        {
            return;
        }
        CheckEvent();
        if (!IsBuffer && _frames.SizeMinusActiveFrame == 2)
        {
            IsPlay = false;
            _isRender = false;
            return;
        }
        if (!IsBuffer)
        {
            SetBufferFrame();
        }
        if (IsBuffer)
        {
            DrawFrame();
        }

    }

    private void SetNodesNewPosition()
    {
        if (_nodesNewPosition == null)
        {
            return;
        }
        ModelNode modelNode = null;
        for (int i = 0; i < _nodesNewPosition.Count; i++)
        {
            modelNode = _modelObject.GetNode(_nodesNewPosition[i]);
            if (modelNode != null)
            {
                Vector3? p_vector = _frames.GetFrame(2)[modelNode.Id];
                _frames.GetFrame(0)[modelNode.Id] = p_vector.Value;
                _frames.GetFrame(1)[modelNode.Id] = p_vector.Value;
            }
        }
        _nodesNewPosition = null;
    }

    /// <summary>The COM's position in each frame of the move as it will play (Moves Information, Physics).</summary>
    private void RecordPredictedPath()
    {
        PredictedPath.Clear();
        var com = _modelObject.CenterOfMassNode;
        if (com == null)
        {
            return;
        }
        for (int i = 2; i < _frames.Size; i++)
        {
            var frame = _frames.GetFrame(i);
            if (frame != null && com.Id < frame.Count && frame[com.Id].HasValue)
            {
                PredictedPath.Add(frame[com.Id].Value);
            }
        }
    }

    public void Clear()
    {
        _animationOld = null;
        Animation = null;
        Sign = 1;
        _isRender = false;
        BoundPlatform = null;
        _speedTween = null;
        _speedTweenOld = _speedTweenOld.Reset();
        Stop();
    }

    public void Play(AnimationInfo info, bool reverse, int firstFrame)
    {
        if (info != null)
        {
            Init(info, reverse, firstFrame);
        }
    }

    private void PlayReaction(AnimationReaction reaction, AnimationEventType type, string data = null)
    {
        if (reaction == null)
        {
            return;
        }
        Trace(reaction, type);
        ModelHuman modelHuman = (ModelHuman)_model;
        modelHuman.VelocityQuads = new Vector3();
        if (reaction.Name == "Death")
        {
            _model.StartPhysics();
            LevelMainController.current.Death(modelHuman, 0);
            return;
        }
        if (reaction.IsTrick)
        {
            TrickAreaRunner.TrickGetByModel(modelHuman);
        }
        if (reaction.IsAnimationArrest)
        {
            modelHuman.Arrest();
        }
        if (reaction.OnEndTrigger && modelHuman != null)
        {
            modelHuman.DelayReaction = reaction;
            return;
        }
        if (reaction.CornerCling)
        {
            CorenerPoint();
        }
        if (reaction.CornerClingV >= 0)
        {
            CornerPoint(reaction.CornerClingV, true);
        }
        if (reaction.CornerClingH >= 0)
        {
            CornerPoint(reaction.CornerClingH, false);
        }
        if (reaction.NodesWi != null && reaction.NodesWi.Count > 0)
        {
            _nodesNewPosition = reaction.NodesWi;
        }
        PlayingReaction = reaction;
        _modelObject.DetectorHorizontalLine.Safe = reaction.SafeHorizontal;
        _modelObject.DetectorVerticalLine.Safe = reaction.SafeVertical;
        if (_model.Type != ModelType.Human)
        {
            return;
        }
        if (type == AnimationEventType.Controller)
        {
            modelHuman.ControllerKeys.ClearAnimation();
        }
        modelHuman.PlayAnimation(reaction);
    }

    public void Stop()
    {
        IsPlay = false;
    }

    private void SetInterruptFrames()
    {
        List<Vector3?> frame = _frames.GetFrame(0);
        List<Vector3?> frame2 = _frames.GetFrame(1);
        ModelNode modelNode = null;
        double num = (_pointFrame + 1) * 0.5f;
        int count = (int)MathF.Min(frame.Count, _modelObject.NodesAll.Count);
        for (int i = 0; i < count; i++)
        {
            modelNode = _modelObject.NodesAll[i];
            double num2 = (modelNode.Start.X - modelNode.End.X) * num;
            double num3 = (modelNode.Start.Y - modelNode.End.Y) * num;
            double num4 = (modelNode.Start.Z - modelNode.End.Z) * num;
            frame[i] = new Vector3(modelNode.Start.X - (float)num2, modelNode.Start.Y - (float)num3, modelNode.Start.Z - (float)num4);
            frame2[i] = new Vector3(modelNode.Start.X + (float)num2, modelNode.Start.Y + (float)num3, modelNode.Start.Z + (float)num4);
        }
    }

    private void SetBufferFrame()
    {
        _pointFrame = (int)(Math.Max(Animation.MidFrames, 1) * LevelMainController.current.slowModeFrames);
        _buffer.InitBuffer(_pointFrame + 1);
        List<Vector3?> activeFrame = _frames.GetActiveFrame(0);
        List<Vector3?> activeFrame2 = _frames.GetActiveFrame(1);
        List<Vector3?> activeFrame3 = _frames.GetActiveFrame(2);
        _buffer.ResizeIfNeed(activeFrame3.Count);
        Vector3 vector3f = new Vector3(0f, 0f, 0f);
        Vector3 vector3f2 = new Vector3(0f, 0f, 0f);
        for (int i = 0; i < activeFrame3.Count; i++)
        {
            if (activeFrame[i].HasValue && activeFrame2[i].HasValue && activeFrame3[i].HasValue)
            {
                Vector3 p_point = activeFrame[i].Value;
                Vector3 vector3f3 = activeFrame2[i].Value;
                Vector3 p_point2 = activeFrame3[i].Value;
                VectorUtils.Middle(p_point, vector3f3, ref vector3f);
                VectorUtils.Middle(vector3f3, p_point2, ref vector3f2);
                if (_pointFrame > -1)
                {
                    for (int j = 0; j <= _pointFrame; j++)
                    {
                        double num = (j + 1f) / (_pointFrame + 1f);
                        double num2 = (1f - num) * (1f - num);
                        double num3 = num * num;
                        double num4 = 2f * num * (1f - num);
                        double p_x = vector3f.X * num2 + vector3f3.X * num4 + vector3f2.X * num3;
                        double p_y = vector3f.Y * num2 + vector3f3.Y * num4 + vector3f2.Y * num3;
                        double p_z = vector3f.Z * num2 + vector3f3.Z * num4 + vector3f2.Z * num3;
                        _buffer.GetFrame(j)[i] = new Vector3((float)p_x, (float)p_y, (float)p_z);
                    }

                }

            }
        }
        _frames.NextActiveFrame();
    }

    public void DrawFrame()
    {
        List<Vector3?> activeFrame = _buffer.GetActiveFrame(0);
        _buffer.NextActiveFrame();

        for (int i = 0; i < activeFrame.Count; i++)
        {
            ModelNode node = _modelObject.GetNode(i);
            if (node == null)
                continue;

            node.EndAssignStart();

            Vector3 position = activeFrame[i].Value;
            node.PositionStart(position.X, position.Y, position.Z);

        }

        if (_buffer.IsBufferEmpty)
        {
            _animationFrame++;

            if (CurrentFrame != _oldFrame)
            {
                _oldFrame = CurrentFrame;
                _isNewFrame = true;
            }
        }
    }

    public void PlaySounds(List<AnimationSound> sounds)
    {
        if (sounds != null)
        {
            float zoom = LocationCamera.Current.Zoom;
            Vector3 start = LocationCamera.Current.Node.Start;
            Vector3 start2 = _model.GetNode("COM").Start;
            double val = 1.5f - Math.Abs(start2.X - start.X) / 760f * zoom;
            double val2 = 1.5f - Math.Abs(start2.Y - start.Y) / 760f * zoom;
            double val3 = Math.Min(1f, Math.Min(val, val2));
            double num = Math.Max(0f, val3);
            if (_model is ModelHuman && (_model as ModelHuman).IsBot)
            {
                num = !(num < 0.5f) ? num / 1.1f : num * 1.5f;
            }
            for (int i = 0; i < sounds.Count; i++)
            {
                sounds[i].Play((float)num);
            }
        }
    }

    public void VelocityQuads()
    {
        if (BoundPlatform != null)
        {
            _speedTween = new Vector3(BoundPlatform.TweenPosition, 0);
        }
        if (!_speedTween.HasValue)
        {
            return;
        }
        Vector2? platformBound = PlatformBound;
        var speed = new Vector3(_speedTween.Value.X, _speedTween.Value.Y, 0);
        if (!platformBound.HasValue)
        {
            _speedTweenOld = _speedTweenOld.Set(_speedTween.Value);
        }
        else
        {
            if (platformBound.Value.X > 0f)
            {
                speed.X = _speedTweenOld.X;
            }
            if (platformBound.Value.Y > 0f)
            {
                speed.Y = _speedTweenOld.Y;
            }
        }
        if (speed.X != 0f || speed.Y != 0f)
        {
            VelocityVector(speed, _targetPointOld);
            VelocityVector(speed, PredictedPath); // the moving platform carries the predicted path too
            VelocityFrames(speed);
            VelocityBuffer(speed);
            VelocityNodes(speed);
            ((ModelHuman)_model).VelocityQuads = speed;
        }
    }

    public void SetVelocityQuads(DetectorEvent @event, bool isSort, QuadRunner platform = null)
    {
        if ((isSort || IsConditionlessPlatformBound(@event)) && (@event == null || @event.Type == DetectorEvent.DetectorEventType.On))
        {
            if (platform == null)
            {
                platform = @event.Platform;
            }
            BoundPlatform = platform;
            if (platform.Sticky)
            {
                _speedTween = new Vector3(platform.TweenPosition, 0);
            }

        }
    }

    public void VelocityVector(Vector3 point, List<Vector3> array)
    {
        for (int i = 0; i < array.Count; i++)
        {
            var v = array[i];
            array[i] = v.Add(point);
        }
    }

    public void VelocityFrames(Vector3 point)
    {
        List<Vector3?> list = null;
        for (int i = 0; i < _frames.Size; i++)
        {
            list = _frames.GetFrame(i);
            for (int j = 0; j < list.Count; j++)
            {
                list[j] += point;
            }
        }
    }

    public void VelocityBuffer(Vector3 point)
    {
        _buffer.VelocityBuffer(point);
    }

    public void VelocityNodes(Vector3 point)
    {
        ModelNode modelNode = null;
        for (int i = 0; i < _modelObject.NodesAll.Count; i++)
        {
            modelNode = _modelObject.NodesAll[i];
            modelNode.Start = modelNode.Start.Add(point);
            modelNode.End = modelNode.End.Add(point);
        }
    }

    public void VelocityPoint()
    {
        if (Animation.Binding)
        {
            int index = Math.Max(0, _animationFrame - 1);
            Vector3 Vector3 = new Vector3(0f, 0f, 0f);
            Vector3 Vector32 = new Vector3(0f, 0f, 0f);
            if (IsOldType(2))
            {
                Vector3 = Vector3.Add(_targetPointOld[index]);
                ReverseVelocity();
                Vector32 = Vector32.Add(_velocityOld[index]);
            }
            else
            {
                Vector3 = Vector3.Add(_frames.GetFrame(2)[_currentNode].Value);
                Vector32 = Vector32.Add(Animation.Velocity);
            }
            Vector32.X *= Sign;
            _velocityOld.Clear();
            _targetPointOld.Clear();
            List<Vector3?> list = null;
            for (int i = 2; i < _frames.Size; i++)
            {
                list = _frames.GetFrame(i);
                Vector3 = Vector3.Add(Vector32);
                Position(Vector3, list);
                Vector32 = Vector32.Add(Animation.Gravity);
                _targetPointOld.Add(new Vector3(Vector3.X, Vector3.Y, Vector3.Z));
                _velocityOld.Add(new Vector3(Vector32.X, Vector32.Y, Vector32.Z));
            }
        }
    }

    public bool IsOldType(int type)
    {
        if (_animationOld == null)
        {
            return false;
        }
        return Animation.Type == type && Animation.Type == _animationOld.Type;
    }

    public void ReverseVelocity()
    {
        if (Sign == 1)
        {
            return;
        }
        for (int i = 0; i < _velocityOld.Count; i++)
        {
            var vector = _velocityOld[i];
            vector.X *= Sign;
            _velocityOld[i] = vector;
        }
    }

    private void CheckEvent()
    {
        CheckController();
        if (_isNewFrame)
        {
            CheckFrame();
            CheckEndFrame();
            _isNewFrame = false;
        }
    }

    private void CheckController()
    {
        if (!_model.IsEnabled || IsDelayReaction)
        {
            return;
        }
        ModelHuman modelHuman = _model as ModelHuman;
        if (modelHuman == null)
        {
            return;
        }
        KeyVariables keyVariables = modelHuman.ControllerKeys.KeyVariables;
        if (keyVariables == null)
        {
            return;
        }
        _forSort.Clear();
        AnimationInterval animationInterval = null;
        AnimationEventKey animationEventKey = null;
        for (int i = 0; i < Intervals.Count; i++)
        {
            animationInterval = Intervals[i];
            for (int j = 0; j < animationInterval.KeyEvents.Count; j++)
            {
                animationEventKey = animationInterval.KeyEvents[j];
                List<AnimationReaction> validateReactions = modelHuman.ControllerKeys.GetValidateReactions(keyVariables, animationEventKey, Sign);
                _forSort.Add(validateReactions);
            }
        }
        AnimationReaction p_reaction = Sort(_forSort);
        PlayReaction(p_reaction, AnimationEventType.Controller, string.Empty);
    }

    public bool CheckCollision(Collision collisionResult, AnimationEventCollision.Type type)
    {
        _forSort.Clear();
        AnimationInterval animationInterval = null;
        AnimationEventCollision animationEventCollision = null;
        for (int i = 0; i < Intervals.Count; i++)
        {
            animationInterval = Intervals[i];
            for (int j = 0; j < animationInterval.CollisionEvents.Count; j++)
            {
                animationEventCollision = animationInterval.CollisionEvents[j];
                if (animationEventCollision.Reaction != null && (animationEventCollision.Types.Count == 0 || animationEventCollision.Types.Contains(type)))
                {
                    _forSort.Add(animationEventCollision.Reaction);
                }
            }
        }
        AnimationDeltaData p_delta = type != AnimationEventCollision.Type.Quad ? null : new AnimationDeltaData(collisionResult.Platform, new Vector3(collisionResult.Point.X, collisionResult.Point.Y, collisionResult.Point.Z), Sign);
        AnimationReaction animationReaction = Sort(_forSort, p_delta, collisionResult.Platform);
        if (type == AnimationEventCollision.Type.Quad)
            SetVelocityQuads(null, animationReaction != null, collisionResult.Platform);
        string empty = string.Empty;
        PlayReaction(animationReaction, AnimationEventType.OnCollision, empty);
        return animationReaction != null;
    }

    public void CheckEndFrame()
    {
        if (!_model.IsEnabled)
        {
            return;
        }
        _forSort.Clear();
        AnimationInterval animationInterval = null;
        for (int i = 0; i < Intervals.Count; i++)
        {
            animationInterval = Intervals[i];
            if (CurrentFrame >= animationInterval.EndFrame)
            {
                for (int j = 0; j < animationInterval.EndEvents.Count; j++)
                {
                    _forSort.Add(animationInterval.EndEvents[j].Reaction);
                }
            }
        }
        if (_forSort.Count > 0)
        {
            AnimationReaction p_reaction = Sort(_forSort);
            PlayReaction(p_reaction, AnimationEventType.OnEnd, string.Empty);
        }
    }

    public AnimationReaction Sort(List<List<AnimationReaction>> reactions, AnimationDeltaData delta = null, QuadRunner platform = null)
    {
        if (reactions.Count <= 0 || _model.Type != ModelType.Human)
        {
            return null;
        }
        var human = (ModelHuman)_model;
        // What the conditions are measured on, as SortAnimation sees it (for Moves Information).
        _sortSnapshot = (
            _modelObject.DetectorHorizontalLine?.Platform, _modelObject.DetectorHorizontalLine?.Position ?? Vector3.Zero,
            _modelObject.DetectorVerticalLine?.Platform, _modelObject.DetectorVerticalLine?.Position ?? Vector3.Zero,
            delta?.Platform, delta?.Position ?? Vector3.Zero,
            human.DeltaVelocity(platform != null ? new Vector3(platform.TweenPosition, 0) : new Vector3()));
        return human.SortAnimation(reactions, delta, platform);
    }

    private (QuadRunner? h, Vector3 hPos, QuadRunner? v, Vector3 vPos, QuadRunner? c, Vector3 cPos, Vector3 velocity) _sortSnapshot;

    /// <summary>DetectorH or DetectorV: which detector fired the event being handled.</summary>
    private string _traceDetector;

    /// <summary>The last reaction played and why (Moves Information, I key).</summary>
    public ReactionTrace LastReaction { get; private set; }

    public void ClearTrace() => LastReaction = null;

    /// <summary>Records the reaction about to play with the interval and event it comes from.</summary>
    private void Trace(AnimationReaction reaction, AnimationEventType type)
    {
        if (Animation == null)
        {
            return;
        }
        string[] names = type switch
        {
            AnimationEventType.Controller => new[] { "Keys" },
            AnimationEventType.DetectorOn or AnimationEventType.DetectorOff => new[] { _traceDetector ?? "DetectorV" },
            AnimationEventType.OnEnd => new[] { "OnEnd" },
            AnimationEventType.OnCollision => new[] { "OnCollision" },
            AnimationEventType.OnArea => new[] { "OnArea", "OnTrigger" },
            AnimationEventType.OnFrame => new[] { "OnFrame" },
            _ => Array.Empty<string>(),
        };
        AnimationInterval foundInterval = null;
        AnimationEvent foundEvent = null;
        // The intervals of the current frame first, then the whole move; an event of the right kind first.
        foreach (var intervals in new[] { new List<AnimationInterval>(Intervals), Animation.Intervals })
        {
            foreach (bool matchKind in new[] { true, false })
            {
                foreach (var interval in intervals)
                {
                    foreach (var e in AllEvents(interval))
                    {
                        if (e.Reaction != null && e.Reaction.Contains(reaction) && (!matchKind || names.Contains(e.Param.XmlName)))
                        {
                            foundInterval = interval;
                            foundEvent = e;
                            goto found;
                        }
                    }
                }
            }
        }
    found:
        var snap = _sortSnapshot;
        LastReaction = new ReactionTrace
        {
            Reaction = reaction,
            FromMove = Animation.Name,
            FromFrame = CurrentFrame,
            Interval = foundInterval,
            Event = foundEvent,
            EventType = type,
            PlatformH = snap.h,
            PositionH = snap.hPos,
            PlatformV = snap.v,
            PositionV = snap.vPos,
            PlatformC = snap.c,
            PositionC = snap.cPos,
            Velocity = snap.velocity,
            Sign = Sign,
        };
    }

    private static IEnumerable<AnimationEvent> AllEvents(AnimationInterval interval)
    {
        foreach (var e in interval.KeyEvents) yield return e;
        foreach (var e in interval.DetectorHEvents) yield return e;
        foreach (var e in interval.DetectorVEvents) yield return e;
        foreach (var e in interval.CollisionEvents) yield return e;
        foreach (var e in interval.FrameEvents) yield return e;
        foreach (var e in interval.AreaEvents) yield return e;
        foreach (var e in interval.EndEvents) yield return e;
    }

    public void CheckFrame()
    {
        if (IsDelayReaction)
        {
            return;
        }
        _forSort.Clear();
        AnimationInterval animationInterval = null;
        AnimationEventFrame animationEventFrame = null;
        for (int i = 0; i < Intervals.Count; i++)
        {
            animationInterval = Intervals[i];
            for (int j = 0; j < animationInterval.FrameEvents.Count; j++)
            {
                animationEventFrame = animationInterval.FrameEvents[j];
                if (animationEventFrame.Frame == CurrentFrame)
                {
                    _forSort.Add(animationEventFrame.Reaction);
                    PlaySounds(animationEventFrame.Sound);
                }
            }
        }
        AnimationReaction p_reaction = Sort(_forSort);
        PlayReaction(p_reaction, AnimationEventType.OnFrame, string.Empty);
    }

    public void Position(Vector3 shift, List<Vector3?> array)
    {
        Vector3 p_vector = shift - array[_currentNode].Value;
        for (int i = 0; i < array.Count; i++)
        {
            array[i] += p_vector;
        }
    }

    public void CheckDetectors(DetectorEvent @event)
    {
        if (!_model.IsEnabled || IsDelayReaction)
        {
            return;
        }
        _forSort.Clear();
        AnimationInterval animationInterval = null;
        AnimationEventDetector animationEventDetector = null;
        AnimationReaction animationReaction = null;
        for (int i = 0; i < Intervals.Count; i++)
        {
            animationInterval = Intervals[i];
            List<AnimationEventDetector> list = !@event.IsVertical ? animationInterval.DetectorHEvents : animationInterval.DetectorVEvents;
            for (int j = 0; j < list.Count; j++)
            {
                animationEventDetector = list[j];
                if (animationEventDetector.Type != 0 && animationEventDetector.Type != @event.Type)
                {
                    continue;
                }
                for (int k = 0; k < animationEventDetector.Reaction.Count; k++)
                {
                    animationReaction = animationEventDetector.Reaction[k];
                    var list1 = new List<AnimationReaction>();
                    if (animationReaction.IsSide(@event.Side, Sign))
                    {
                        list1.Add(animationReaction);
                    }
                    _forSort.Add(list1);

                }
            }
        }
        _traceDetector = @event.IsVertical ? "DetectorV" : "DetectorH";
        AnimationReaction animationReaction2 = Sort(_forSort, null, @event.Platform);
        _forSort.Clear();
        if (animationReaction2 == null)
        {
            SetVelocityQuads(@event, false);
            return;
        }
        ModelHuman modelHuman = (ModelHuman)_model;
        Vector3 vector3f = _modelObject.Velocity + modelHuman._VelocityQuadCurrent;
        Vector3 vector3f2 = new Vector3(0f, 0f, 0f);
        if (LandingPositionDetectorH >= 0f && @event.IsHorizontal)
        {
            vector3f2.X = LandingPositionDetectorH * Sign - 4f * vector3f.X;
        }
        if (LandingPositionDetectorV >= 0f && @event.IsVertical)
        {
            vector3f2.Y = LandingPositionDetectorV - 4f * vector3f.Y;
        }
        @event.DeltaPosition(vector3f2);

        SetVelocityQuads(@event, true);
        AnimationEventType p_type;
        switch (@event.Type)
        {
            case DetectorEvent.DetectorEventType.On:
                p_type = AnimationEventType.DetectorOn;
                break;
            case DetectorEvent.DetectorEventType.Off:
                p_type = AnimationEventType.DetectorOff;
                break;
            default:
                p_type = AnimationEventType.Controller;
                break;
        }
        string empty = string.Empty;
        PlayReaction(animationReaction2, p_type, empty);
    }

    public void CheckArea(QuadRunner runner)
    {
        if (!_isRender)
        {
            return;
        }
        _forSort.Clear();
        foreach (var interval in Intervals)
        {
            foreach (var areaEvent in interval.AreaEvents)
            {
                _forSort.Add(areaEvent.Reaction);
            }
        }
        PlayReaction(Sort(_forSort), AnimationEventType.OnArea);
    }

    public void CorenerPoint()
    {
        DetectorLine detectorLine = _modelObject.DetectorVerticalLine;
        QuadRunner data = detectorLine.Node.Data;
        if (data != null)
        {
            Vector3 vector3f = new Vector3(detectorLine.Position.X, detectorLine.Position.Y, detectorLine.Position.Z);
            vector3f.Z = 0f;
            Vector3? vector3f3 = data.Corner(Sign, 0);
            if (vector3f3.HasValue)
            {
                detectorLine.Node.Start = detectorLine.Node.Start.Add(vector3f3.Value - vector3f);
                detectorLine.Node.EndAssignStart();
            }
        }
    }

    public void CornerPoint(int cornernum, bool isDetectorV)
    {
        DetectorLine detectorLine = !isDetectorV ? _modelObject.DetectorHorizontalLine : _modelObject.DetectorVerticalLine;
        QuadRunner data = detectorLine.Node.Data;
        if (data != null)
        {
            Vector3 vector3f = new Vector3(detectorLine.Position.X, detectorLine.Position.Y, detectorLine.Position.Z);
            vector3f.Z = 0f;
            Vector3 vector3f2 = vector3f;
            Vector3? vector3f3 = data.Corner(Sign, cornernum);
            if (vector3f3.HasValue)
            {
                detectorLine.Node.Start = detectorLine.Node.Start.Add(vector3f3.Value - vector3f2);
                detectorLine.Node.EndAssignStart();
            }
        }
    }

    public bool IsConditionlessPlatformBound(DetectorEvent dEvent)
    {
        AnimationInterval animationInterval = null;
        for (int i = 0; i < Intervals.Count; i++)
        {
            animationInterval = Intervals[i];
            if ((dEvent == null && animationInterval.ConditionlessBoundC) || (dEvent != null && dEvent.IsVertical && animationInterval.ConditionlessBoundV) || (dEvent != null && dEvent.IsHorizontal && animationInterval.ConditionlessBoundH))
            {
                return true;
            }
        }
        return false;
    }

    public Vector3 Shift(List<Vector3?> frameNodes)
    {
        return frameNodes[_currentNode].Value - _frames.GetFrame(2)[_currentNode].Value;
    }

    public void ShiftPoint()
    {
        ReverseFrames();
        Vector3 p_vector = Shift(_frames.GetFrame(1));
        List<Vector3?> list = null;
        for (int i = 2; i < _frames.Size; i++)
        {
            list = _frames.GetFrame(i);
            ShiftNodesPoint(list, p_vector);
        }
    }

    public void ReverseFrames()
    {
        if (Sign == 1)
        {
            return;
        }
        List<Vector3?> list = null;
        for (int i = 2; i < _frames.Size; i++)
        {
            list = _frames.GetFrame(i);
            for (int j = 0; j < list.Count; j++)
            {
                var vector = list[j].Value;
                vector.X *= Sign;
                list[j] = vector;
            }
        }
    }

    public void ShiftNodesPoint(List<Vector3?> array, Vector3 value)
    {
        for (int i = 0; i < array.Count; i++)
        {
            array[i] += value;
        }
    }

    public void MirrorNode(string name1, string name2)
    {
        if (Sign == -1)
        {
            string text = name1;
            name1 = name2;
            name2 = text;
        }
        ModelNode node = _modelObject.GetNode(name1);
        ModelNode node2 = _modelObject.GetNode(name2);
        if (node == null || node2 == null)
        {
            return;
        }
        Vector3 start = node.Start;
        Vector3 start2 = node2.Start;
        List<Vector3?> frame = _frames.GetFrame(2);
        Vector3 vector3f = frame[node.Id].Value;
        Vector3 vector3f2 = frame[node2.Id].Value;
        if (Animation.Mirror)
        {
            _isMirror = start.X >= start2.X != vector3f.X >= vector3f2.X;
        }
        if (!_isMirror)
        {
            return;
        }
        _currentNode = _modelObject.GetNodeIdByName(BothNodeName(Animation.PivotNode));
        List<int[]> bothNodeList = _modelObject.BothNodeList;
        List<Vector3?> list = null;
        for (int i = 2; i < _frames.Size; i++)
        {
            list = _frames.GetFrame(i);
            for (int j = 0; j < bothNodeList.Count; j++)
            {
                int[] array = bothNodeList[j];
                Vector3 value = list[array[0]].Value;
                list[array[0]] = list[array[1]];
                list[array[1]] = value;
            }
        }
    }

    public string BothNodeName(string name)
    {
        StringBuilder stringBuilder = new StringBuilder(name);
        switch (stringBuilder[name.Length - 1])
        {
            case '1':
                stringBuilder[name.Length - 1] = '2';
                break;
            case '2':
                stringBuilder[name.Length - 1] = '1';
                break;
        }
        return stringBuilder.ToString();
    }
}
