#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Animation;
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

        public const float Player = -15f;
    }

    protected Location _Location;

    protected LocationCamera _Camera;
    private bool _pauseRender;

    private bool _tutorialPause;

    private uint _frameCount;

    public static LevelMainController current { get; private set; }

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

    public float slowModeFrames { get; set; }

    public static bool IsHunterMode { get; private set; }

    public bool CanPauseOrReload { get; private set; }

    public bool IsDeath { get; private set; }

    public bool IsWin { get; private set; }

    public static void Init(string level, bool hunterMode = false)
    {
        IsHunterMode = hunterMode;
        current = new LevelMainController();
        current.slowModeFrames = 1;
        current.InitLevel(level);
    }

    public void InitLevel(string level)
    {
        _Location = CreateLocation(level);
        AddModeles();
        InitCoins();
        CreateCamera();
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

    }

    private void CreateCamera()
    {
        _Camera = new LocationCamera();
        _Camera.Init();
        _Camera.Zooming(LocationCamera.CurrentZoom, true);
    }

    public void Render()
    {
        if (!_pauseRender && !_tutorialPause)
        {
            _Camera.UpdatePosition();
            _Location.Render();
            _frameCount++;
        }
    }

    public void HandleNewInput(KeyVariables key)
    {
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
        _frameCount = 0;
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

    public static void Clear()
    {
        current = null;
    }
}
