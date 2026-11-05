// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;
using System.Numerics;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Animation.Events;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.User;
using AnimationInfo = Vectorier.Gameplay.Animation.AnimationInfo;
using Collision = Vectorier.Gameplay.Result.Collision;

namespace Vectorier.Gameplay.Models
{
    public class ModelHuman : Model
    {
        private ControllerKeys _controllerKeys;

        private ControllerAnimations _controllerAnimations;

        private ControllerTrigger _controllerTrigger;

        private ControllerModelEffect _controllerModelEffect;

        private UserData _userData;

        private bool _IsGadget;

        private List<ItemScoreRunner> _collectedBonuses = new List<ItemScoreRunner>();

        private List<TrickAreaRunner> _collectedTricks = new List<TrickAreaRunner>();

        private AnimationReaction _DelayReaction;

        private float _TimeOut;

        private float _TimeKill = 120;

        private float _LiveTime;

        private ControllerStatistics _controllerStatistics;

        private bool _isDeath;

        public Vector3 _VelocityQuadCurrent = new Vector3();

        public Vector3 _VelocityQuadPrevious = new Vector3();

        private SpawnRunner _Respawn;

        private bool _IsArrest;

        public Rectangle _CollisionBox = new Rectangle();

        private bool _isRectangleFixed;

        private Rectangle _rectangle = new Rectangle();

        private bool _isDelayEnd;

        private const string StartAnimation = "JumpOff";

        public const int MainModelNodesCount = 46;

        public ControllerKeys ControllerKeys => _controllerKeys;

        public ControllerAnimations ControllerAnimations => _controllerAnimations;

        public ControllerTrigger ControllerTrigger => _controllerTrigger;

        public ControllerModelEffect controllerModelEffect => _controllerModelEffect;

        public UserData UserData => _userData;

        public bool IsGadget
        {
            get => _IsGadget;
            set => _IsGadget = value;
        }

        public override bool IsEnabled => _isDelayEnd ? base.IsEnabled : false;

        public int collectedBonusesCount => _collectedBonuses.Count;

        public int collectedTricksCount => _collectedTricks.Count;

        public int CollectedPoints
        {
            get
            {
                int points = 0;
                foreach (var item in _collectedBonuses)
                {
                    points += item.score;
                }
                foreach (var item in _collectedTricks)
                {
                    points += item.score;
                }
                return points;
            }
        }

        public AnimationReaction DelayReaction
        {
            get => _DelayReaction;
            set => _DelayReaction = value;
        }

        public ControllerStatistics controllerStatistics => _controllerStatistics;

        public bool IsDeath
        {
            get => _isDeath;
            set => _isDeath = value;
        }

        public Vector3 VelocityQuadCurrent => _VelocityQuadCurrent;

        public Vector3 VelocityQuadPrevious => _VelocityQuadPrevious;

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

        public bool IsPlay => _controllerAnimations.IsPlay;

        public int Sign => _controllerAnimations == null ? 1 : _controllerAnimations.Sign == 1 ? 1 : -1;

        public string AnimationName => _controllerAnimations.Name;

        public int CurrentFrame => _controllerAnimations.CurrentFrame;

        public bool IsSelf => _userData.IsSelf;

        public bool SafeInterval => _controllerAnimations.SafeInterval;

        public bool LockInterval => _controllerAnimations.LockInterval;

        public bool IsTrick => _userData.IsTrick;

        public List<string> Arrests => _userData.Arrests;

        public List<string> Murders => _userData.Murders;

        public string ModelName => _userData.Name;

        public int AI => _userData.AI;

        public bool IsLoss => _userData.IsLost;

        public bool IsVictory => _userData.IsVictory;

        public string BirthSpawn => _userData.BirthSpawn;

        public bool IsBot => _userData.IsBot;

        public int PlatformAnticipationFrames => _controllerAnimations.Animation.PlatformAnticipationFrames;

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

        public bool IsActionInterval => _controllerAnimations.IsActionInterval;

        public AnimationInfo Animation => _controllerAnimations.Animation;

        public bool IsDelayEnd
        {
            get => _isDelayEnd;
            set => _isDelayEnd = value;
        }

        public ModelHuman(UserData userData)
            : base(userData.Skins, ModelType.Human)
        {
            _userData = userData;
            _LiveTime = _userData.LiveTime;
            _ModelObject.Color = userData.Color;
            Name = _userData.Name;
            Init();

        }

        public override void Init()
        {
            base.Init();
            _controllerAnimations = new ControllerAnimations(this);
            _controllerKeys = new ControllerKeys(this);
            _controllerTrigger = new ControllerTrigger(this);
            _controllerModelEffect = new ControllerModelEffect(this);
            _controllerStatistics = new ControllerStatistics();
            _ModelObject.IsVisible = false;
        }

        public AnimationInfo AnimationByName(string name)
        {
            return _userData.Animation(name);
        }

        public void OnActiveArea(QuadRunner trigger)
        {
            if (_controllerAnimations.IsPlay)
            {
                _controllerAnimations.CheckArea(trigger);
            }
        }

        public void CheckDelayAction(QuadRunner quad)
        {
            if (_DelayReaction != null && _DelayReaction.CheckNameHash(quad.NameHash))
            {
                PlayAnimation(_DelayReaction);
                _DelayReaction = null;
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
            if ((detectorVerticalLine.Platform != platform || !_controllerAnimations.IsSafeV) && (detectorHorizontalLine.Platform != platform || !_controllerAnimations.IsSafeH) && !SafeInterval && !_controllerAnimations.CheckCollision(@event.Data, AnimationEventCollision.Type.Quad))
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
            if (_Respawn != null)
            {
                spawnRunner = _Respawn;
            }
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
            _controllerAnimations.CheckCollision(collision, p_type);
        }

        public void PlayAnimation(AnimationReaction reaction, int sign)
        {
            if (reaction == null)
            {
                return;
            }
            _controllerAnimations.Stop();
            _ControllerPhysics.Stop();
            PlayAnimation(reaction.Name, reaction.Reverse, reaction.FirstFrame);
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
            PlayAnimation(_userData.Animation(name), reverse, firstFrame);
        }

        public void PlayAnimation(AnimationInfo info, bool reverse, int firstFrame)
        {
            _ControllerPhysics.Stop();
            _controllerAnimations.Stop();
            _controllerAnimations.Play(info, reverse, firstFrame);
            _controllerStatistics.SetAnimation(info);
        }

        public void StopAnimation()
        {
            _controllerAnimations.Stop();
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
            float autoPositionDetectorH = _controllerAnimations.AutoPositionDetectorH;
            float autoPositionDetectorV = _controllerAnimations.AutoPositionDetectorV;
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
            if (!_isDelayEnd)
            {
                if (_TimeOut < _userData.StartTime * 60 * LevelMainController.current.slowModeFrames)
                {
                    _TimeOut++;
                    return;
                }
                _isDelayEnd = true;
                _ModelObject.IsVisible = true;
            }
            _ControllerPhysics.Render();
            _controllerTrigger.Render();
            if (_controllerAnimations.IsPlay)
            {
                _controllerKeys.Render();
                _controllerAnimations.Render();
                DetectorsVelocity();
            }
            _ModelObject.RenderMacroNode();
            _ModelObject.RenderDetector();
            _controllerModelEffect.Render();
            SetCollisionBox();
            _controllerStatistics.SetPosition(_ModelObject.CenterOfMassNode.Start, Sign);
            if (!_isDeath)
            {
                return;
            }
            _LiveTime--;
            if (_LiveTime > 0)
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
            _controllerAnimations.Clear();
            _ControllerPhysics.Start();
        }

        public void CollectBonus(ItemScoreRunner bonus)
        {
            _collectedBonuses.Add(bonus);
            _controllerStatistics.SetBonus(bonus);
        }

        public void AddCoin(CoinRunner coin)
        {
            _controllerStatistics.SetCoin(coin);
            //UserDataManager.Instance.MainData.AddCoins(coin.score);
            //UserDataManager.Instance.SaveUserDate();
        }

        public override void Reset()
        {
            _ControllerPhysics.Stop();
            _controllerAnimations.Clear();
            _ControllerCollisions.Reset();
            _controllerKeys.Reset();
            _controllerTrigger.Reset();
            _controllerModelEffect.Reset();
            _collectedBonuses.Clear();
            _collectedTricks.Clear();
            _ModelObject.Reset();
            _TimeOut = 0;
            _isDelayEnd = false;
            _DelayReaction = null;
            _isDeath = false;
            _LiveTime = _userData.LiveTime;
            _IsArrest = false;
            _ModelObject.IsVisible = false;
            IsEnabled = false;
            _VelocityQuadCurrent = _VelocityQuadCurrent.Reset();
            _VelocityQuadPrevious = _VelocityQuadPrevious.Reset();
            if (IsSelf)
            {
                //UserDataManager.Instance.Stats.Add(_controllerStatistics);
                //UserDataManager.Instance.SaveUserDate();
                _controllerStatistics.Reset();
            }
        }

        public void Death(GameEndType gameEndType)
        {
            if (_isDeath) return;
            _isDeath = true;
            _controllerStatistics.SetGameOver(gameEndType);
        }

        //public void UseGadget(Gadget gadget)
        //{
        //    _controllerStatistics.SetGadget(gadget.gadgetType);
        //}

        public void CollectTrick(TrickAreaRunner trick)
        {
            _collectedTricks.Add(trick);
            _controllerStatistics.SetTrick(trick);
        }

        private void CalcRectangle()
        {
            _rectangle.Set(_controllerAnimations.BoundingBox);
            _rectangle.Origin.X += (float)_ModelObject.CenterOfMassNode.Start.X;
            _rectangle.Origin.Y += (float)_ModelObject.CenterOfMassNode.Start.Y;
        }
    }
}
