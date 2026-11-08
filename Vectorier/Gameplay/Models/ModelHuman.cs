#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Animation.Events;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.User;
using AnimationInfo = Vectorier.Gameplay.Animation.AnimationInfo;
using Collision = Vectorier.Gameplay.Result.Collision;

namespace Vectorier.Gameplay.Models;

public class ModelHuman : Model
{
    private List<ItemScoreRunner> _collectedBonuses = new List<ItemScoreRunner>();

    private List<TrickAreaRunner> _collectedTricks = new List<TrickAreaRunner>();
    private float _timeOut;
    private float _liveTime;
    public Vector3 _VelocityQuadCurrent = new Vector3();

    public Vector3 _VelocityQuadPrevious = new Vector3();

    public Rectangle _CollisionBox = new Rectangle();

    private bool _isRectangleFixed;

    private Rectangle _rectangle = new Rectangle();

    public ControllerKeys ControllerKeys { get; private set; }

    public ControllerAnimations ControllerAnimations { get; private set; }

    public ControllerTrigger ControllerTrigger { get; private set; }

    public ControllerModelEffect controllerModelEffect { get; private set; }

    public UserData UserData { get; }

    public bool IsGadget { get; set; }

    public override bool IsEnabled => IsDelayEnd ? base.IsEnabled : false;

    public AnimationReaction DelayReaction { get; set; }

    public ControllerStatistics controllerStatistics { get; private set; }

    public bool IsDeath { get; set; }

    public Vector3 VelocityQuads
    {
        set
        {
            _VelocityQuadPrevious.X = _VelocityQuadCurrent.X;
            _VelocityQuadPrevious.Y = _VelocityQuadCurrent.Y;

            _VelocityQuadCurrent.X = value.X;
            _VelocityQuadCurrent.Y = value.Y;
        }
    }

    public Rectangle CollisionBox => _CollisionBox;

    public bool IsPlay => ControllerAnimations.IsPlay;

    public int Sign => ControllerAnimations == null ? 1 : ControllerAnimations.Sign == 1 ? 1 : -1;

    public string AnimationName => ControllerAnimations.Name;

    public int CurrentFrame => ControllerAnimations.CurrentFrame;

    public bool IsSelf => UserData.IsSelf;

    public bool SafeInterval => ControllerAnimations.SafeInterval;

    public bool LockInterval => ControllerAnimations.LockInterval;

    public bool IsTrick => UserData.IsTrick;

    public List<string> Arrests => UserData.Arrests;

    public List<string> Murders => UserData.Murders;

    public string ModelName => UserData.Name;

    public int AI => UserData.AI;

    public string BirthSpawn => UserData.BirthSpawn;

    public bool IsBot => UserData.IsBot;

    public int PlatformAnticipationFrames => ControllerAnimations.Animation.PlatformAnticipationFrames;

    public bool fixRectangle
    {
        set
        {
            _isRectangleFixed = value;
            if (value)
            {
                CalcRectangle();
            }
        }
    }

    public override Rectangle Rectangle
    {
        get
        {
            if (!_isRectangleFixed)
            {
                CalcRectangle();
            }
            return _rectangle;
        }
    }

    public AnimationInfo Animation => ControllerAnimations.Animation;

    public bool IsDelayEnd { get; set; }

    public ModelHuman(UserData userData)
        : base(userData.Skins, ModelType.Human)
    {
        UserData = userData;
        _liveTime = UserData.LiveTime;
        _ModelObject.Color = userData.Color;
        Name = UserData.Name;
        Init();

    }

    public override void Init()
    {
        base.Init();
        ControllerAnimations = new ControllerAnimations(this);
        ControllerKeys = new ControllerKeys(this);
        ControllerTrigger = new ControllerTrigger(this);
        controllerModelEffect = new ControllerModelEffect(this);
        controllerStatistics = new ControllerStatistics();
        _ModelObject.IsVisible = false;
    }

    public void OnActiveArea(QuadRunner trigger)
    {
        if (ControllerAnimations.IsPlay)
        {
            ControllerAnimations.CheckArea(trigger);
        }
    }

    public void CheckDelayAction(QuadRunner quad)
    {
        if (DelayReaction != null && DelayReaction.CheckNameHash(quad.NameHash))
        {
            PlayAnimation(DelayReaction);
            DelayReaction = null;
        }
    }

    public override void OnCollisionPlatform(ModelEvent<Collision> @event)
    {

        if (!IsPlay)
        {
            return;
        }
        var platform = @event.Data.Platform;
        DetectorLine detectorVerticalLine = _ModelObject.DetectorVerticalLine;
        DetectorLine detectorHorizontalLine = _ModelObject.DetectorHorizontalLine;
        if ((detectorVerticalLine.Platform != platform || !ControllerAnimations.IsSafeV) && (detectorHorizontalLine.Platform != platform || !ControllerAnimations.IsSafeH) && !SafeInterval && !ControllerAnimations.CheckCollision(@event.Data, AnimationEventCollision.Type.Quad))
        {
            StartPhysics();
            if (IsBot)
            {
                Death(GameEndType.GE_DEATH);
                return;
            }
            LocationCamera.Current.Stop();
            SoundsManager.PlaySounds(SoundType.bodyfall1);
            LevelMainController.current.Death(this, 1);
        }


    }

    public void Play(SpawnRunner spawn)
    {
        _ControllerPhysics.NodeReset();
        Position(new Vector3(spawn.LocalPosition, 0));
        PlayAnimation(spawn.Reaction);
        IsEnabled = true;
    }

    public void Arrest()
    {
        if (ArrestAreaRunner.ActiveArrest != null)
        {
            ArrestAreaRunner.ActiveArrest.Arresting();
        }
    }

    public void Start(SpawnRunner spawnRunner)
    {
        if (spawnRunner != null)
            Play(spawnRunner);
    }

    public static double Distance(ModelHuman model1, ModelHuman model2, string nodeName1 = "COM", string nodeName2 = "COM")
    {
        ModelNode modelNode = model1.GetNode(nodeName1);
        ModelNode modelNode2 = model2.GetNode(nodeName2);
        if (modelNode == null || modelNode2 == null)
        {
            return double.NaN;
        }
        return Vector3.Distance(modelNode.Start, modelNode2.End);
    }

    public override void OnCollisionModel(Collision collision)
    {
        AnimationEventCollision.Type p_type;
        switch (collision.Model.Type)
        {
            case ModelType.Primitive:
                p_type = AnimationEventCollision.Type.Primitive;
                break;
            case ModelType.PrimitiveAnimated:
                p_type = AnimationEventCollision.Type.PrimitiveAnimated;
                break;
            default:
                p_type = AnimationEventCollision.Type.Quad;
                break;
        }
        ControllerAnimations.CheckCollision(collision, p_type);
    }

    public void PlayAnimation(AnimationReaction reaction)
    {
        if (reaction == null)
        {
            return;
        }
        if (reaction.Name == "Death")
        {
            StartPhysics();
            LevelMainController.current.Death(this, 1);
            return;
        }
        PlayAnimation(reaction.Name, reaction.Reverse, reaction.FirstFrame);
    }

    public void PlayAnimation(string name, bool reverse = false, int firstFrame = -1)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }
        PlayAnimation(UserData.Animation(name), reverse, firstFrame);
    }

    public void PlayAnimation(AnimationInfo info, bool reverse, int firstFrame)
    {
        _ControllerPhysics.Stop();
        ControllerAnimations.Stop();
        ControllerAnimations.Play(info, reverse, firstFrame);
        controllerStatistics.SetAnimation(info);
    }

    public AnimationReaction SortAnimation(List<List<AnimationReaction>> reactions, AnimationDeltaData delta = null, QuadRunner platform = null)
    {
        var deltaH = DeltaDetector(_ModelObject.DetectorHorizontalLine);
        var deltaV = DeltaDetector(_ModelObject.DetectorVerticalLine);
        var vector = new Vector3();
        if (platform != null)
            vector = vector.Add(platform.TweenPosition);
        var velocity = DeltaVelocity(vector);
        var areas = _ControllerCollisions.ActiveAreas;
        return AnimationReaction.GetReaction(reactions, deltaH, deltaV, delta, areas, this, velocity);
    }

    public Vector3 DeltaVelocity(Vector3 value)
    {
        Vector3 Vector3 = _ModelObject.Velocity + _VelocityQuadCurrent - value;
        Vector3.X *= Sign;
        return Vector3;
    }

    public void DetectorsVelocity()
    {
        float autoPositionDetectorH = ControllerAnimations.AutoPositionDetectorH;
        float autoPositionDetectorV = ControllerAnimations.AutoPositionDetectorV;
        if (autoPositionDetectorH != -1.0 || autoPositionDetectorV != -1.0)
        {
            Vector3 Vector3 = _ModelObject.Velocity + _VelocityQuadCurrent;
            Vector3 start = _ModelObject.CenterOfMassNode.Start;
            if (autoPositionDetectorV >= 0f)
            {
                _ModelObject.DetectorVerticalNode.Start.X = start.X + 4f * Vector3.X + Sign * autoPositionDetectorV;
                _ModelObject.DetectorVerticalNode.Start.Y = start.Y + 4f * Vector3.Y;
            }
            if (autoPositionDetectorH > 0f)
            {
                Vector3 start2 = _ModelObject.ToeRight.Start;
                Vector3 start3 = _ModelObject.ToeLeft.Start;
                _ModelObject.DetectorHorizontalNode.Start.X = Sign != 1 ? Math.Min(start2.X, start3.X) + Vector3.X * 4 : Math.Max(start2.X, start3.X) + Vector3.X * 4;
            }
        }
    }

    public AnimationDeltaData DeltaDetector(DetectorLine detector)
    {
        if (detector == null || detector.Platform == null)
        {
            return null;
        }
        return new AnimationDeltaData(detector.Platform, detector.Position, Sign);
    }

    public override void Render(List<QuadRunner> platforms = null)
    {
        if (!base.IsEnabled)
        {
            return;
        }
        if (!IsDelayEnd)
        {
            if (_timeOut < UserData.StartTime * 60 * LevelMainController.current.slowModeFrames)
            {
                _timeOut++;
                return;
            }
            IsDelayEnd = true;
            _ModelObject.IsVisible = true;
        }
        _ControllerPhysics.Render();
        ControllerTrigger.Render();
        if (ControllerAnimations.IsPlay)
        {
            ControllerKeys.Render();
            ControllerAnimations.Render();
            DetectorsVelocity();
        }
        _ModelObject.RenderMacroNode();
        _ModelObject.RenderDetector();
        controllerModelEffect.Render();
        SetCollisionBox();
        controllerStatistics.SetPosition(_ModelObject.CenterOfMassNode.Start, Sign);
        if (!IsDeath)
        {
            return;
        }
        _liveTime--;
        if (_liveTime > 0)
        {
            return;
        }
        IsEnabled = false;
        _ModelObject.IsVisible = false;
    }

    public void SetCollisionBox()
    {
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        float minX = float.MaxValue;
        float minY = float.MaxValue;

        for (int i = 0; i < _ModelObject.CollisibleNodes.Count; i++)
        {
            Vector3 start = _ModelObject.CollisibleNodes[i].Start;

            if (start.X >= maxX)
                maxX = start.X;

            if (start.Y >= maxY)
                maxY = start.Y;

            if (start.X <= minX)
                minX = start.X;

            if (start.Y <= minY)
                minY = start.Y;
        }

        _CollisionBox.Origin.X = minX;
        _CollisionBox.Origin.Y = minY;

        _CollisionBox.Size.X = maxX - minX;
        _CollisionBox.Size.Y = maxY - minY;
    }

    public override void Draw()
    {
        base.Draw();
        if (VectorGame.RenderBoxes)
        {
            var vs = Sets.Current.Containers[1];
            Raylib.DrawRectangleLinesEx(new Vectorier.Gameplay.Rl.Rectangle(vs.TransformPoint(_CollisionBox.Origin), _CollisionBox.Size * vs.FrameScale), 2, Color.Yellow);
        }
    }

    public override void StartPhysics()
    {
        ControllerAnimations.Clear();
        _ControllerPhysics.Start();
    }

    public void CollectBonus(ItemScoreRunner bonus)
    {
        _collectedBonuses.Add(bonus);
        controllerStatistics.SetBonus(bonus);
    }

    public void AddCoin(CoinRunner coin)
    {
        controllerStatistics.SetCoin(coin);
    }

    public override void Reset()
    {
        _ControllerPhysics.Stop();
        ControllerAnimations.Clear();
        _ControllerCollisions.Reset();
        ControllerKeys.Reset();
        ControllerTrigger.Reset();
        controllerModelEffect.Reset();
        _collectedBonuses.Clear();
        _collectedTricks.Clear();
        _ModelObject.Reset();
        _timeOut = 0;
        IsDelayEnd = false;
        DelayReaction = null;
        IsDeath = false;
        _liveTime = UserData.LiveTime;
        _ModelObject.IsVisible = false;
        IsEnabled = false;
        _VelocityQuadCurrent = _VelocityQuadCurrent.Reset();
        _VelocityQuadPrevious = _VelocityQuadPrevious.Reset();
        if (IsSelf)
        {
            controllerStatistics.Reset();
        }
    }

    public void Death(GameEndType gameEndType)
    {
        if (IsDeath) return;
        IsDeath = true;
        controllerStatistics.SetGameOver(gameEndType);
    }

    public void CollectTrick(TrickAreaRunner trick)
    {
        _collectedTricks.Add(trick);
        controllerStatistics.SetTrick(trick);
    }

    private void CalcRectangle()
    {
        _rectangle.Set(ControllerAnimations.BoundingBox);
        _rectangle.Origin.X += (float)_ModelObject.CenterOfMassNode.Start.X;
        _rectangle.Origin.Y += (float)_ModelObject.CenterOfMassNode.Start.Y;
    }
}
