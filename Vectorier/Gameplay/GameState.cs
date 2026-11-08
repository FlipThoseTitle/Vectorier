// The game ran one level per process: its state is static. In the editor a game starts many times (Play, Hunter Mode,
// Play Here, the Move Visualizer), so everything a level leaves behind is cleared before the next one.
#nullable disable warnings
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger;
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay;

public static class GameState
{
    /// <summary>
    /// Clears the previous level's state. <paramref name="projectChanged"/>: the project's own files (trigger
    /// templates, localization) are read again too.
    /// </summary>
    public static void ResetForNewGame(bool projectChanged)
    {
        SoundsManager.StopMusic();
        GlobalObjects.Drawables.Clear();
        GlobalObjects.Updatables.Clear();
        GlobalObjects.InitAfterAdding = false;
        Model.ModelsAll.Clear();
        RunnerRender.Reset();
        TriggerActionsRenderer.Reset();
        TE_StartGame.ResetAll();
        TE_Line.ResetCounter();
        LocationCamera.ResetAll();
        ArrestAreaRunner.ActiveArrest = null;
        TrickAreaRunner.Current = null;
        VisualRunner.SortingOrder = 0;
        GameplayUI.Reset();
        ResourceManager.ClearTextureCache();
        AtlasDecoder.ClearCache();
        if (projectChanged)
        {
            ResourceManager.ClearSoundCache();
            ResourceManager.ClearMusicCache();
            Localization.Reset();
        }
        // TriggerTemplates.xml: the editor may have changed it since the last game.
        TemplateModule.Reload();
    }
}
