// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay;

public class LevelMainController
{
    public static class Layer
    {
        public const float Base = -1f;

        public const float Debug = -10f;

        public const float Player = -15f;
    }

    protected Location _Location;

    protected LocationCamera _Camera;

    private static bool _isHunterMode;

    private bool _pauseRender;

    private bool _tutorialPause;

    private uint _FrameCount;

    private bool _inputBeen;

    //private ControllerGadgets _controllerGadgets;

    public Model debugModel;

    private string _trackIDForAnalytic;

    private bool _canShowAd;

    public static LevelMainController current
    {
        get;
        private set;
    }

    public Location Location => _Location;

    public LocationCamera Camera => _Camera;

    private ModelHuman UserModel
    {
        get
        {
            foreach (var model in _Location.Models)
            {
                if (model.IsBot)
                {
                    continue;
                }
                return model;
            }
            return null;
        }
    }

    public bool slowMode
    {
        set
        {
            float num = 10;
            if (!value)
            {
                num = 1;
            }
            slowModeFrames = num;
        }
    }

    public float slowModeFrames
    {
        get;
        set;
    }

    public static bool IsHunterMode => _isHunterMode;

    public bool pauseRender
    {
        get => _pauseRender;
        set
        {
            _pauseRender = value;
        }
    }

    public bool tutorialPause => _tutorialPause;

    public uint FrameCount => _FrameCount;

    public bool CanPauseOrReload
    {
        get;
        private set;
    }

    public bool IsDeath
    {
        get;
        private set;
    }

    public bool IsWin
    {
        get;
        private set;
    }

    public int SkipPlayerFrames
    {
        get;
        set;
    }

    //public ControllerGadgets controllerGadgets => _controllerGadgets;

    public static void Init(string level, bool hunterMode = false)
    {
        _isHunterMode = hunterMode;
        current = new LevelMainController();
        current.slowModeFrames = 1;
        current.InitLevel(level);
        //current.pauseRender = true;
    }

    public void InitLevel(string level)
    {
        _canShowAd = false;
        _Location = CreateLocation(level);
        AddModeles();
        InitCoins();
        CreateCamera();
        //_controllerGadgets = new ControllerGadgets();
        SoundsManager.PlayMusic(_Location.Music);
        _Location.Start();
        CanPauseOrReload = true;
        GC.Collect();
    }

    private Location CreateLocation(string level)
    {
        var xmlPath = Path.Combine(VectorPaths.XmlLevels, level);
        return new Location(xmlPath);
    }

    private void AddModeles()
    {
        foreach (var data in _Location.UserData)
        {
            _Location.CreateModelHuman(data);
        }
    }

    private void InitCoins()
    {
        //var coinrunners = Location.GetAllCoins();
        //CoinSetter.SetCoins(coinrunners, Location.Sets.TotalCoins);
        //CoinsSystem.CurrentType = CoinsSystemType.CST_DEFAULT;
        //var settings = LocationManager.Instance.coinsSystem.GetCoinsSettings(CoinsSystem.CurrentType, CoinType.CT_DEFAULT);
        //var settings1 = LocationManager.Instance.coinsSystem.GetCoinsSettings(CoinsSystem.CurrentType, CoinType.CT_BRONZE);
        //var settings2 = LocationManager.Instance.coinsSystem.GetCoinsSettings(CoinsSystem.CurrentType, CoinType.CT_GOLD);

        //List<CoinRunner> activeCoins = new List<CoinRunner>(coinrunners);
        //foreach (var coinRunner in coinrunners)
        //{
        //    activeCoins.Add(coinRunner);
        //    InitCoins(activeCoins, settings2, new Color(1, 0.8117647f, 0.28235295f));
        //    InitCoins(activeCoins, settings1, new Color(0.9647059f, 0.2901961f, 0.27450982f));
        //}
    }

    //private static void InitCoins(List<CoinRunner> activeCoins, CoinsSettings coinsSettings, Color color)
    //{
    //    if (coinsSettings.count > 0)
    //    {
    //        for (int i = 0; i < coinsSettings.count; i++)
    //        {
    //            var random = Random.Range(0, 100);
    //            if (random % 100 < coinsSettings.chance * 100)
    //            {
    //                var index = Random.Range(0, activeCoins.Count);
    //                var coin = activeCoins[index];
    //                coin.color = color;
    //                coin.score = coinsSettings.nominal;
    //                activeCoins.RemoveAt(index);
    //            }
    //        }
    //    }
    //}

    private void CreateCamera()
    {
        _Camera = new LocationCamera();
        _Camera.Init();
        //_Camera.Layers(_Location.Sets);
        _Camera.Zooming(LocationCamera.CurrentZoom, true);
        //_Camera.UpdatePosition();
    }

    public void Render()
    {
        if (!_pauseRender && !_tutorialPause)
        {
            _Camera.UpdatePosition();
            _Location.Render();
            _FrameCount++;
        }
    }

    public void HandleNewInput(KeyVariables key)
    {
        _inputBeen = true;
        UserModel.ControllerKeys.SetKeyVariable(key);
    }

    public void TutorialAreaActivate(TutorialAreaRunner area)
    {
        slowModeFrames = 10;
        GameplayUI.ShowTutorial(area.Key, area.Description);
    }

    public void TutorialLockGame()
    {
        _tutorialPause = true;
    }

    public void TutorialUnLockGame()
    {
        _tutorialPause = false;
        slowModeFrames = 1;
        GameplayUI.HideTutorial();
    }

    public bool HasCurrentLocationEverBeenCompleted()
    {
        //var info = UserDataManager.Instance.CurrentBalanceLocation.CurrentStoryModeStoryInfos[UserDataManager.RuntimeInfo.CurrentStory];
        //var stats = UserDataManager.Instance.GameStats.GetTrackStats(info.Name);
        //if (stats != null)
        //{
        //    return stats.Stars > 0;
        //}
        return false;
    }

    public void RefreshTricks()
    {
        _Location.RefreshTricks();
    }

    public void Win(ModelHuman modelHuman, float time)
    {
        if (IsWin || IsDeath)
        {
            return;
        }
        modelHuman.IsGadget = false;
        IsWin = true;
        CanPauseOrReload = false;
        Wait(time, Reload);
    }

    //public void GoToWinScene()
    //{
    //    ClearScene();
    //    var storyInfo = UserDataManager.Instance.CurrentBalanceLocation.CurrentStoryModeStoryInfos[UserDataManager.RuntimeInfo.CurrentStory];
    //    if (storyInfo.CutsceneEnd != null)
    //    {
    //        var data = new VideoScreenPayloadData(storyInfo.CutsceneEnd, () =>
    //        {
    //            Game.Instance.ScreenManager.Show<PostGameplayWinScreen>(true, false);
    //        });
    //        Game.Instance.ScreenManager.Show<VideoScreen, VideoScreenPayloadData>(data, true, false);
    //        return;
    //    }
    //    Game.Instance.ScreenManager.Show<PostGameplayWinScreen>(true, false);
    //}

    public void Murder(ModelHuman modelHuman)
    {
        if (!IsWin && !IsDeath)
        {
            {
                CanPauseOrReload = false;
                modelHuman.IsGadget = false;
                modelHuman.Death(GameEndType.GE_MURDER);
                Console.WriteLine("Murder");
                IsDeath = true;
                Wait(1, Reload);
            }
        }
    }

    public void Death(ModelHuman modelHuman, float time)
    {
        if (!IsWin && !IsDeath)
        {
            if (!modelHuman.IsBot)
            {
                CanPauseOrReload = false;
                modelHuman.IsGadget = false;
                modelHuman.Death(GameEndType.GE_DEATH);
                modelHuman.StartPhysics();
                //Debug.Assert(false);
                Console.WriteLine("Death");
                IsDeath = true;
                Wait(1, Reload);
            }
        }
    }

    public void Loss(ModelHuman modelHuman, float time)
    {
        if (!IsWin && !IsDeath)
        {
            {
                LocationCamera.Current.Stop();
                CanPauseOrReload = false;
                modelHuman.IsGadget = false;
                modelHuman.Death(GameEndType.GE_LOSS);
                Console.WriteLine("Loss");
                IsDeath = true;
                Wait(1, Reload);
            }
        }
    }

    public void Arrest(ModelHuman modelHuman)
    {
        if (!IsWin && !IsDeath)
        {
            CanPauseOrReload = false;
            modelHuman.IsGadget = false;
            Console.WriteLine("Arrest");
            IsDeath = true;

            Wait(1, Reload);
        }
    }

    /// <summary>Seconds left before the automatic restart after a Win / Loss / Death / Arrest (negative: none).</summary>
    private float _reloadDelay = -1f;

    /// <summary>True while a Win / Loss / Death / Arrest is waiting for its automatic restart.</summary>
    public bool IsReloadPending => _reloadDelay >= 0f;

    /// <summary>
    /// Starts the countdown before the automatic restart. It is counted down by <see cref="TickReloadDelay"/> from the
    /// main loop, which skips it while the game is paused (Esc), so pausing freezes the countdown and unpausing resumes
    /// it. It also runs on the main thread, like everything else that touches the level.
    /// </summary>
    void Wait(float seconds, Action onEnd)
    {
        _reloadDelay = MathF.Max(0f, seconds);
        _reloadAction = onEnd;
    }

    private Action _reloadAction;

    /// <summary>Advances the restart countdown by <paramref name="seconds"/> (called only while the game runs).</summary>
    public void TickReloadDelay(float seconds)
    {
        if (_reloadDelay < 0f)
        {
            return;
        }
        _reloadDelay -= seconds;
        if (_reloadDelay <= 0f)
        {
            _reloadDelay = -1f;
            var action = _reloadAction ?? Reload;
            _reloadAction = null;
            action();
        }
    }

    //public void ClearScene()
    //{

    //    Object.Destroy(GameObject.Find("Level_root_object"));

    //    ResourceManager.ClearTextureCache();
    //    AtlasDecoder.ClearCache();

    //    SceneManager.UnloadSceneAsync("Level");

    //    Resources.UnloadUnusedAssets();
    //    GC.Collect();
    //}

    public void ReloadButton()
    {
        Reload();
    }

    /// <summary>
    /// Reload Moves (debug panel, no key): moves.xml and the animation .bin files are read again, then the level
    /// restarts with them. For modders changing moves without restarting the game.
    /// </summary>
    public void ReloadMovesButton()
    {
        Console.WriteLine("Reload moves");
        if (!AnimationLoader.Current.ReloadMoves(out string? error))
        {
            Console.WriteLine("moves.xml could not be reloaded: " + error);
            VectorGame.ShowNotice("moves.xml could not be reloaded (the previous moves are kept): " + error, true);
            return;
        }
        // Trick areas load their trick's frames when the level is built: again for the new moves.
        foreach (var area in _Location.Sets.AllRunners.OfType<AreaRunner>())
            area.LoadBinaryIfTrick();
        Reload();
        VectorGame.ShowNotice($"Moves reloaded ({Animations.Animation.Count} moves)", false);
    }

    protected void Reload()
    {
        Console.WriteLine("Reload");
        _reloadDelay = -1f; // a manual restart (R) during the delay cancels the automatic one
        GameplayUI.Reset();
        _Location.Reload();
        foreach (var model in _Location.Models)
        {
            model.ControllerAnimations?.ClearTrace(); // Moves Information: nothing played yet
        }
        TutorialUnLockGame();
        InitCoins();
        _Camera.Reset();
        _Camera.Zooming(LocationCamera.CurrentZoom, true);
        _Location.Start();
        _Camera.UpdatePosition();
        _pauseRender = false;
        IsWin = false;
        slowModeFrames = 1;
        CanPauseOrReload = true;
        IsDeath = false;
        _FrameCount = 0;
    }

    public List<ModelHuman> GetModelsByNames(List<string> modelHumanNames)
    {
        var list = new List<ModelHuman>();
        foreach (var modelName in modelHumanNames)
        {
            var model = GetModelByName(modelName);
            if (model != null)
                list.Add(model);
        }
        return list;
    }

    public ModelHuman GetModelByName(string name)
    {
        return _Location.GetModelByName(name);
    }

    public void SelectDebugModel()
    {
    }

    //private void CheckInputBeen()
    //{
    //    if (_inputBeen)
    //    {
    //        TryShowAd();
    //        _inputBeen = false;
    //        return;
    //    }
    //    Game.Instance.ScreenManager.Show<GameplayPauseScreen>(false, false);
    //    pauseRender = true;
    //}

    private void TryShowAd()
    {
        //AD RELATED
    }

    //private void OnInterstitialEnd()
    //{
    //    UIBlocker.UnBlock();
    //    pauseRender = false;
    //}

    public static void Clear()
    {
        //FirebaseHandler.instance.SetCustomKey("Track", "-");
        current = null;
    }
}
