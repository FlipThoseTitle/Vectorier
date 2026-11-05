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
using System.Globalization;
using System.Numerics;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay
{
    /// <summary>
    /// A small debug panel button: "[Key] Name". Dark and translucent so it stays out of the way; a lit marker shows
    /// whether a toggle is on, and a disabled button is dimmed.
    /// </summary>
    class Button
    {
        public const float Height = 24;
        const float FontSize = 15;
        const float Pad = 8;

        public Vectorier.Gameplay.Rl.Rectangle Rectangle;

        readonly string Key;
        readonly string Text;

        /// <summary>Optional live text (e.g. "Anti-alias: High"); replaces Text when set.</summary>
        public Func<string>? DynamicText;

        public Action OnClick;

        /// <summary>Toggle state (null: a plain action button).</summary>
        public Func<bool>? IsOn;

        /// <summary>When it returns false the button is dimmed and does nothing.</summary>
        public Func<bool>? IsEnabled;

        bool IsHovered;
        bool IsPressed;

        static readonly Color Background = new(20, 20, 24, 150);
        static readonly Color BackgroundHover = new(45, 45, 55, 190);
        static readonly Color BackgroundPressed = new(70, 70, 85, 210);
        static readonly Color KeyColor = new(255, 200, 80, 255);
        static readonly Color OnColor = new(90, 200, 120, 255);
        static readonly Color OffColor = new(110, 110, 120, 255);

        public Button(float x, float y, float width, string key, string text, Action onClick)
        {
            Rectangle = new Vectorier.Gameplay.Rl.Rectangle(x, y, width, Height);
            Key = key;
            Text = text;
            OnClick = onClick;
        }

        bool Enabled => IsEnabled?.Invoke() ?? true;

        /// <summary>True while the pointer is over it (clicks there are not meant for the game).</summary>
        public bool IsHot => IsHovered;

        public void Update()
        {
            if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Rectangle))
            {
                IsHovered = true;
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    IsPressed = true;
                }
                else
                {
                    if (IsPressed && Enabled)
                        OnClick?.Invoke();
                    IsPressed = false;
                }
            }
            else
            {
                IsHovered = false;
                IsPressed = false;
            }
        }

        public void Draw()
        {
            bool enabled = Enabled;
            var bg = !enabled ? Background : IsPressed ? BackgroundPressed : IsHovered ? BackgroundHover : Background;
            Raylib.DrawRectangleRounded(Rectangle, 0.25f, 4, bg);

            float x = Rectangle.X + Pad;
            float textY = Rectangle.Y + (Height - FontSize) / 2;
            byte alpha = (byte)(enabled ? 255 : 110);

            // Toggle marker: a small lit / unlit dot.
            if (IsOn != null)
            {
                bool on = IsOn();
                var dot = on ? OnColor : OffColor;
                Raylib.DrawCircleV(new Vector2(x + 3, Rectangle.Y + Height / 2), 3.5f, new Color(dot.R, dot.G, dot.B, alpha));
            }
            x += 13; // action buttons keep the same text column as the toggles

            string key = Key;
            if (key.Length > 0) // a button without a key shows only its name
            {
                GameFont.DrawUi(key, x, textY, FontSize, new Color(KeyColor.R, KeyColor.G, KeyColor.B, alpha));
                x += GameFont.UiWidth(key, FontSize) + 7;
            }
            GameFont.DrawUi(DynamicText?.Invoke() ?? Text, x, textY, FontSize, new Color((byte)235, (byte)235, (byte)235, alpha));
        }
    }

    public class VectorGame
    {
        int ScreenWidth;
        int ScreenHeight;

        public Camera2D Camera;

        public static readonly float OrtographicSize = 214;

        public static VectorGame Instance;

        public bool ImagesVisible = true;

        public Rectangle CameraRectangle;

        bool pause = false;
        bool stepOneFrame = false;

        public static bool RenderPlatforms;
        public static bool RenderTriggers;
        public static bool RenderAreas;

        /// <summary>N cycles: no nodes, every node, only COM / DetectorH / DetectorV / Camera.</summary>
        public enum NodeDisplay { None, All, Important }

        public static NodeDisplay NodeMode;

        static readonly string[] NodeModeNames = { "Off", "All", "Important" };

        /// <summary>G: a cross line through the screen center.</summary>
        public static bool ShowGrid;

        /// <summary>I: Moves Information overlay (see MoveConditionsOverlay).</summary>
        public enum MovesInfoMode { None, ReactionCondition, Physics, Safe, PlatformBound }

        public static MovesInfoMode MovesInfo;

        static readonly string[] MovesInfoNames = { "None", "Reaction", "Physics", "Safe", "Platform Bound" };
        public static bool RenderEdges;
        public static bool RenderDetectors;
        public static bool RenderBoxes;

        /// <summary>The debug settings (the game's debugsettings.txt): kept by the editor's settings.</summary>
        public static Func<string> LoadSettingsText;

        /// <summary>The editor's Reload Moves: moves.xml and the animations are read from the project again first.</summary>
        public static Action ReloadMovesRequested;
        public static Action<string> SaveSettingsText;

        public static string Settings;

        /// <summary>H: hides every debug overlay on the screen (buttons, animation name, FPS).</summary>
        bool hideDebugUi = false;

        readonly List<Button> Buttons = new();
        readonly Dictionary<KeyboardKey, Button> ButtonKeys = new();

        /// <summary>
        /// The game in the editor's Game View: no window of its own (the view is its window, see
        /// <see cref="Frame"/>), the editor's textures and the game mixer.
        /// </summary>
        public VectorGame()
        {
            Raylib.InitAudioDevice();
            Raylib.SetMasterVolume(0.5f); // the game is too loud at full volume

            LoadSettings();
            Resize(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());

            ResourceManager.LoadResources();
        }

        /// <summary>The Game View was resized (or went full screen): the camera keeps the game's 428-unit height.</summary>
        void Resize(int width, int height)
        {
            ScreenWidth = width;
            ScreenHeight = height;
            Camera = new Camera2D(new Vector2(width / 2f, height / 2f), Vector2.Zero, 0, height / (OrtographicSize * 2));
            CameraRectangle = GetCameraWorldRect(Camera);
            SetButtons();
        }

        public static Rectangle GetCameraWorldRect(Camera2D camera)
        {
            Vector2 topLeft = Raylib.GetScreenToWorld2D(
                new Vector2(0, 0),
                camera
            );

            Vector2 bottomRight = Raylib.GetScreenToWorld2D(
                new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()),
                camera
            );

            float x = MathF.Min(topLeft.X, bottomRight.X);
            float y = MathF.Min(topLeft.Y, bottomRight.Y);
            float width = MathF.Abs(bottomRight.X - topLeft.X);
            float height = MathF.Abs(bottomRight.Y - topLeft.Y);

            return new Rectangle(x, y, width, height);
        }

        /// <summary>
        /// Parses moves.xml and every move's .bin (again, replacing the moves parsed before, when they changed).
        /// </summary>
        public static void LoadMoves()
        {
            if (Animations.Animation.Count > 0)
            {
                AnimationLoader.Current._xmlPath = VectorPaths.Animations;
                AnimationLoader.Current.ReloadAnimations();
            }
            else
            {
                AnimationLoader.Current.Init();
            }

            // Every move's .bin is independent: parse them on all cores.
            var animations = Animations.ToList();
            Parallel.ForEach(animations, animation => animation.LoadBin());
        }

        /// <summary>
        /// Starts a level (the Run of the game before its loop). <paramref name="loadMoves"/>: moves.xml and the
        /// animations are parsed (only needed when they changed since the last game).
        /// </summary>
        public void Start(string level, bool hunterMode, bool loadMoves)
        {
            if (loadMoves)
            {
                LoadMoves();
            }

            LevelMainController.Init(level, hunterMode);

            GlobalObjects.Init();

            GlobalObjects.InitAfterAdding = true;
        }

        /// <summary>
        /// One frame of the game (one turn of its loop), drawn into the Game View: input, the logic ticks due, then
        /// the level, the game's UI and the debug UI. <paramref name="width"/> × <paramref name="height"/> is the view.
        /// </summary>
        public void Frame(Avalonia.Media.DrawingContext context, int width, int height)
        {
            Raylib.SetTarget(context, width, height);
            if (width != ScreenWidth || height != ScreenHeight)
                Resize(width, height);
            Raylib.PollInputEvents();
            // The UI (text, buttons) is drawn with the editor's defaults (smooth); Q only changes how the level is drawn
            // (DrawWorldAntiAliased). Nothing is set here: an option set outside wins over the same one set inside.
            using (context.PushRenderOptions(new Avalonia.Media.RenderOptions()))
            {
                try
                {
                    Raylib.UpdateMusicStream(SoundsManager.CurrentMusic);

                    if (InputManager.WasKeyPressedThisFrame(KeyboardKey.Escape))
                    {
                        pause = !pause;
                    }

                    if (InputManager.WasKeyPressedThisFrame(KeyboardKey.F))
                    {
                        stepOneFrame = true;
                    }

                    if (InputManager.WasKeyPressedThisFrame(KeyboardKey.R))
                    {
                        LevelMainController.current.ReloadButton();
                    }

                    // Debug toggles and actions: same as clicking their button.
                    foreach (var (key, button) in ButtonKeys.ToList()) // a button may rebuild the panel (Full Screen)
                    {
                        if (key is KeyboardKey.F or KeyboardKey.S or KeyboardKey.Escape or KeyboardKey.R)
                            continue; // handled above / below as before
                        if (InputManager.WasKeyPressedThisFrame(key))
                            button.OnClick?.Invoke();
                    }

                    if (InputManager.WasKeyPressedThisFrame(KeyboardKey.Up))
                    {
                        LevelMainController.current.HandleNewInput(new Controllers.KeyVariables(KeyboardKey.Up));
                    }
                    if (InputManager.WasKeyPressedThisFrame(KeyboardKey.Down))
                    {
                        LevelMainController.current.HandleNewInput(new Controllers.KeyVariables(KeyboardKey.Down));
                    }
                    if (InputManager.WasKeyPressedThisFrame(KeyboardKey.Left))
                    {
                        LevelMainController.current.HandleNewInput(new Controllers.KeyVariables(KeyboardKey.Left));
                    }
                    if (InputManager.WasKeyPressedThisFrame(KeyboardKey.Right))
                    {
                        LevelMainController.current.HandleNewInput(new Controllers.KeyVariables(KeyboardKey.Right));
                    }

                    if (InputManager.WasKeyPressedThisFrame(KeyboardKey.S))
                    {
                        ToggleSlowMode();
                    }

                    if (!hideDebugUi)
                        UpdateButtons();

                    // The game logic advances in fixed 1/60 s ticks. When a frame takes longer than that (a slow PC, a
                    // heavy scene), the missed ticks are caught up so the game keeps its real speed instead of slowing
                    // down with the frame rate. Paused: nothing runs, except one tick for Next Frame (F).
                    int ticks;
                    if (pause)
                    {
                        tickAccumulator = 0;
                        ticks = stepOneFrame ? 1 : 0;
                    }
                    else
                    {
                        ticks = TicksThisFrame();
                    }

                    for (int tick = 0; tick < ticks; tick++)
                    {
                        LevelMainController.current.Render();

                        for (int i = 0; i < GlobalObjects.Updatables.Count; i++)
                        {
                            GlobalObjects.Updatables[i].Update();
                        }

                        // The delay before the automatic restart (Win / Loss / Death) only runs with the game: Esc
                        // freezes it, unpausing resumes it.
                        LevelMainController.current.TickReloadDelay(TickSeconds);
                        GameplayUI.Tick();
                    }
                    stepOneFrame = false;

                    // Anti-aliasing: the level is first drawn into a bigger texture (outside BeginDrawing).
                    RenderWorldSupersampled();

                    Raylib.BeginDrawing();
                    {
                        Raylib.ClearBackground(Color.Gray);

                        DrawWorldAntiAliased();

                        ModelHuman player = LevelMainController.current.Location.GetUserModel();
                        string animation = "null";
                        if (player != null && player.ControllerAnimations != null && player.ControllerAnimations.Animation != null)
                        {
                            animation = player.ControllerAnimations.CurrentFrame + " - " + player.ControllerAnimations.Animation.Name;
                        }

                        // The game's own UI: trigger messages and tutorial hints.
                        GameplayUI.Draw();

                        if (ShowGrid)
                            DrawGrid();

                        // Names and outlines of the debug shapes shown (screen space, like the editor's labels).
                        DrawDebugLabels();

                        switch (MovesInfo)
                        {
                            case MovesInfoMode.ReactionCondition:
                                MoveConditionsOverlay.Draw(Camera);
                                MovesInfoOverlays.DrawVelosity();
                                break;
                            case MovesInfoMode.Physics:
                                MovesInfoOverlays.DrawPhysics(Camera);
                                break;
                            case MovesInfoMode.Safe:
                                MovesInfoOverlays.DrawSafe(Camera);
                                break;
                            case MovesInfoMode.PlatformBound:
                                MovesInfoOverlays.DrawPlatformBound(Camera);
                                break;
                        }

                        if (!hideDebugUi)
                        {
                            GameFont.DrawUiBold(animation, 0, Raylib.GetScreenHeight() - 30, 30, Color.White);

                            DrawButtons();

                            DrawNotice();

                            // Same place and size as raylib's FPS counter, in Arial and always white.
                            GameFont.DrawUiBold($"{Raylib.GetFPS()} FPS", 0, 0, 20, Color.White);
                        }
                    }
                    Raylib.EndDrawing();
                }
                catch
                {
                    // A frame cut short by an error: what it pushed is undone first (the host stops the game).
                    Raylib.PopAllStates();
                    throw;
                }
            }
            Raylib.SetTarget(null, width, height);
        }

        /// <summary>The game was left: its settings are kept for the next one.</summary>
        public void Close()
        {
            SaveSettings();
            SoundsManager.StopMusic();
            Raylib.CloseAudioDevice();
        }

        void SetButtons()
        {
            Buttons.Clear();
            ButtonKeys.Clear();

            const float width = 200, gap = 4, margin = 10, sectionGap = 12;
            float x = ScreenWidth - width - margin;
            float y = margin;

            void Add(KeyboardKey? key, string label, string text, Action onClick, Func<bool>? isOn = null, Func<bool>? isEnabled = null)
            {
                var button = new Button(x, y, width, label, text, onClick) { IsOn = isOn, IsEnabled = isEnabled };
                Buttons.Add(button);
                if (key is KeyboardKey k)
                    ButtonKeys[k] = button;
                y += Button.Height + gap;
            }

            // What is drawn over the level.
            Add(KeyboardKey.T, "T", "Triggers", () => RenderTriggers = !RenderTriggers, () => RenderTriggers);
            Add(KeyboardKey.A, "A", "Areas", () => RenderAreas = !RenderAreas, () => RenderAreas);
            Add(KeyboardKey.P, "P", "Platforms", () => RenderPlatforms = !RenderPlatforms, () => RenderPlatforms);
            Add(KeyboardKey.N, "N", "Nodes", () => NodeMode = (NodeDisplay)(((int)NodeMode + 1) % NodeModeNames.Length), () => NodeMode != NodeDisplay.None);
            Buttons[^1].DynamicText = () => "Nodes: " + NodeModeNames[(int)NodeMode];
            Add(KeyboardKey.E, "E", "Edges", () => RenderEdges = !RenderEdges, () => RenderEdges);
            Add(KeyboardKey.D, "D", "Detectors", () => RenderDetectors = !RenderDetectors, () => RenderDetectors);
            Add(KeyboardKey.B, "B", "Bounding", () => RenderBoxes = !RenderBoxes, () => RenderBoxes);
            Add(KeyboardKey.I, "I", "Moves Info", () => MovesInfo = (MovesInfoMode)(((int)MovesInfo + 1) % MovesInfoNames.Length), () => MovesInfo != MovesInfoMode.None);
            Buttons[^1].DynamicText = () => "Moves: " + MovesInfoNames[(int)MovesInfo];
            Add(KeyboardKey.G, "G", "Center Line", () => ShowGrid = !ShowGrid, () => ShowGrid);

            // Playback.
            y += sectionGap - gap;
            Add(KeyboardKey.Escape, "Esc", "Pause", () => pause = !pause, () => pause);
            Add(KeyboardKey.F, "F", "Next Frame", () => stepOneFrame = true, isEnabled: () => pause);
            Add(KeyboardKey.S, "S", "Slow Mode", ToggleSlowMode, () => LevelMainController.current?.slowModeFrames == 10);
            Add(KeyboardKey.R, "R", "Restart", () => LevelMainController.current.ReloadButton());
            // No key: it reads every move again, which takes a moment (for modders editing moves.xml or .bin files).
            Add(null, "", "Reload Moves", () =>
            {
                // In the editor the files are read again first (it then calls ReloadMovesButton).
                if (ReloadMovesRequested != null) ReloadMovesRequested();
                else LevelMainController.current.ReloadMovesButton();
            });
            Add(KeyboardKey.H, "H", "Hide UI", () => hideDebugUi = !hideDebugUi);
            Add(KeyboardKey.Z, "Z", "Force Blaster", ForceBlast, isEnabled: () => ForceBlastTarget() != null);

            // Display.
            y += sectionGap - gap;
            Add(KeyboardKey.Q, "Q", "Anti-alias", ToggleAntiAlias);
            Buttons[^1].DynamicText = () => "Anti-alias: " + AntiAliasNames[AntiAlias];
            Add(KeyboardKey.F11, "F11", "Full Screen", ToggleFullScreen, () => Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode));
            Add(KeyboardKey.M, "M", "Mute Music", SoundsManager.ToggleMusicMute, () => SoundsManager.MusicMuted);
        }

        // ================= FORCE BLASTER ================= //

        /// <summary>The game's GadgetKillBot: how near a bot must be (between the COMs).</summary>
        const int ForceBlasterDistance = 300;

        /// <summary>
        /// GadgetKillBot.IsCanUse: the player is alive and the first bot that is alive and has started (its delay over)
        /// is within reach. The editor's charges are unlimited (the game spends a GADGET_FORCEBLASTER from the shop).
        /// </summary>
        static ModelHuman ForceBlastTarget()
        {
            var controller = LevelMainController.current;
            var location = controller?.Location;
            var self = location?.GetUserModel();
            if (location == null || self == null || controller.IsDeath || self.IsDeath)
                return null;
            foreach (var bot in location.GetAllBotModels())
            {
                if (bot != null && !bot.IsDeath && bot.IsDelayEnd)
                {
                    return ModelHuman.Distance(self, bot) < ForceBlasterDistance ? bot : null;
                }
            }
            return null;
        }

        /// <summary>
        /// Z: GadgetKillBot.Play. The bot goes into physics and dies (murder), every edge gets an impulse of 20 away
        /// from the player's COM, and the antibot wave plays on the player.
        /// </summary>
        static void ForceBlast()
        {
            var bot = ForceBlastTarget();
            if (bot == null)
                return;
            var self = LevelMainController.current.Location.GetUserModel();
            var com = self.GetNode("COM");
            bot.StartPhysics();
            bot.Death(GameEndType.GE_MURDER);
            foreach (var edge in bot.ModelObject.EdgesAll)
            {
                var vector = edge.Center - com.Start;
                if (vector.LengthSquared() > 0)
                    vector = Vector3.Normalize(vector);
                vector *= 20;
                bot.Strike(edge, edge.Center, vector);
            }
            bot.ModelObject.RenderMacroNode();
            bot.ControllerPhysics.Render();
            bot.ModelObject.RenderMacroNode();
            self.controllerModelEffect.RunAntibotEffect();
        }

        // ================= NOTICE ================= //

        static string? _notice;
        static bool _noticeIsError;
        static double _noticeUntil;

        /// <summary>A short line over the move name (Reload Moves: done, or why moves.xml could not be read).</summary>
        public static void ShowNotice(string text, bool isError)
        {
            _notice = text;
            _noticeIsError = isError;
            _noticeUntil = Raylib.GetTime() + (isError ? 8 : 3);
        }

        void DrawNotice()
        {
            if (_notice == null || Raylib.GetTime() > _noticeUntil)
                return;
            const float size = 18;
            float y = Raylib.GetScreenHeight() - 30 - size - 8;
            float w = GameFont.UiBoldWidth(_notice, size);
            Raylib.DrawRectangleRounded(new Vectorier.Gameplay.Rl.Rectangle(4, y - 4, w + 12, size + 8), 0.3f, 4, new Color(20, 20, 24, 190));
            GameFont.DrawUiBold(_notice, 10, y, size, _noticeIsError ? new Color(255, 120, 110, 255) : new Color(140, 230, 160, 255));
        }

        void ToggleSlowMode()
        {
            LevelMainController.current.slowModeFrames = LevelMainController.current.slowModeFrames == 10 ? 1 : 10;
        }

        // ================= FIXED TIME STEP ================= //

        /// <summary>Length of one game logic tick: the game was made for 60 updates per second.</summary>
        public const float TickSeconds = 1f / 60f;

        /// <summary>Most ticks run in one frame: below 15 FPS the game slows down rather than freezing to catch up.</summary>
        const int MaxTicksPerFrame = 4;

        float tickAccumulator;

        /// <summary>
        /// How many logic ticks this frame needs to keep the game at real speed. At a steady 60 FPS it is always 1
        /// (frame times are rounded to the nearest tick, so the small jitter of a 60 Hz frame does not alternate 0
        /// and 2); at 30 FPS it is 2. A single very long frame (loading, a dragged window) counts as one tick.
        /// </summary>
        int TicksThisFrame()
        {
            float frame = Raylib.GetFrameTime();
            if (frame <= 0 || frame > TickSeconds * MaxTicksPerFrame * 2)
            {
                tickAccumulator = 0;
                return 1;
            }
            tickAccumulator += frame;
            int ticks = (int)MathF.Round(tickAccumulator / TickSeconds);
            if (ticks > MaxTicksPerFrame)
            {
                tickAccumulator = 0;
                return MaxTicksPerFrame;
            }
            tickAccumulator -= ticks * TickSeconds;
            return ticks;
        }

        // ================= CENTER LINE ================= //

        static readonly Color GridCenter = new(255, 255, 255, 140);

        /// <summary>G: a white translucent cross line through the exact screen center.</summary>
        void DrawGrid()
        {
            float cx = ScreenWidth / 2f, cy = ScreenHeight / 2f;
            Raylib.DrawLineEx(new Vector2(cx, 0), new Vector2(cx, ScreenHeight), 1.5f, GridCenter);
            Raylib.DrawLineEx(new Vector2(0, cy), new Vector2(ScreenWidth, cy), 1.5f, GridCenter);
        }

        // ================= ANTI-ALIASING ================= //

        /// <summary>0 = Off, 1 = 2× supersampling, 2 = 4× supersampling (Q).</summary>
        public static int AntiAlias;

        static readonly string[] AntiAliasNames = { "Off", "Medium", "High" };

        /// <summary>The editor's drawing smooths edges itself (no render textures there).</summary>
        const bool EditorDrawsAntiAliased = true;

        RenderTexture2D sceneTarget;
        bool sceneTargetReady;
        int sceneScale;

        /// <summary>4×: the scene is halved twice; this holds the 2× step.</summary>
        RenderTexture2D halfTarget;
        bool halfTargetReady;

        /// <summary>Supersampling factor for the current setting and screen (capped to what a GPU texture allows).</summary>
        int SupersampleFactor()
        {
            // In the editor the drawing itself is anti-aliased (Avalonia smooths every edge: see Frame), so the level
            // is never drawn into a bigger texture first. Off / Medium / High set how the edges and images are drawn.
            if (EditorDrawsAntiAliased)
                return 1;
            int factor = AntiAlias switch { 1 => 2, 2 => 4, _ => 1 };
            int largest = Math.Max(ScreenWidth, ScreenHeight);
            while (factor > 1 && largest * factor > 8192)
                factor--;
            return factor;
        }

        void ToggleAntiAlias()
        {
            AntiAlias = (AntiAlias + 1) % AntiAliasNames.Length;
        }

        /// <summary>
        /// Draws a render texture as an exact copy (blending off: source color and alpha replace the destination).
        /// The scene texture's color is already the final, blended color, but its alpha is not 1 wherever something
        /// translucent was drawn (with normal blending the alpha channel is blended too: a·a + 1·(1−a) &lt; 1). Drawn
        /// with normal blending, those pixels let the gray background show through again, and textures, triggers,
        /// areas and every other translucent thing looked paler with anti-aliasing on.
        /// </summary>
        static void DrawTextureCopy(Texture2D texture, int width, int height)
        {
            Rlgl.SetBlendFactors(1 /* GL_ONE */, 0 /* GL_ZERO */, 0x8006 /* GL_FUNC_ADD */);
            Raylib.BeginBlendMode(BlendMode.Custom);
            // Render textures are stored upside down: a negative source height flips them back.
            Raylib.DrawTexturePro(texture,
                new Vectorier.Gameplay.Rl.Rectangle(0, 0, texture.Width, -texture.Height),
                new Vectorier.Gameplay.Rl.Rectangle(0, 0, width, height),
                Vector2.Zero, 0, Color.White);
            Raylib.EndBlendMode();
        }

        static bool EnsureTarget(ref RenderTexture2D target, ref bool ready, int width, int height)
        {
            if (ready && target.Texture.Width == width && target.Texture.Height == height)
                return true;
            if (ready)
                Raylib.UnloadRenderTexture(target);
            target = Raylib.LoadRenderTexture(width, height);
            ready = target.Id != 0;
            if (ready)
                Raylib.SetTextureFilter(target.Texture, TextureFilter.Bilinear);
            return ready;
        }

        /// <summary>
        /// Models are drawn with plain triangles, circles and thick lines, which raylib does not smooth. With
        /// anti-aliasing on, the level is drawn at 2× or 4× the screen size and scaled down with filtering: the
        /// jagged edges of the models (and of rotated images) become smooth. The UI is drawn on top at screen size.
        /// Each halving is one bilinear draw (bilinear at exactly half the size averages each 2×2 block), so 4× is
        /// two cheap passes instead of rebuilding a full mipmap chain of the big texture every frame.
        /// </summary>
        void RenderWorldSupersampled()
        {
            int factor = SupersampleFactor();
            if (factor <= 1)
            {
                ReleaseSceneTarget();
                return;
            }
            int width = ScreenWidth * factor, height = ScreenHeight * factor;
            if (!EnsureTarget(ref sceneTarget, ref sceneTargetReady, width, height))
            {
                AntiAlias = 0; // the GPU refused: back to direct drawing
                return;
            }
            sceneScale = factor;

            var camera = Camera;
            camera.Offset *= factor;
            camera.Zoom *= factor;

            Raylib.BeginTextureMode(sceneTarget);
            Raylib.ClearBackground(Color.Gray);
            Raylib.BeginMode2D(camera);
            DrawWorld();
            Raylib.EndMode2D();
            Raylib.EndTextureMode();

            if (factor == 4 && EnsureTarget(ref halfTarget, ref halfTargetReady, ScreenWidth * 2, ScreenHeight * 2))
            {
                Raylib.BeginTextureMode(halfTarget);
                DrawTextureCopy(sceneTarget.Texture, ScreenWidth * 2, ScreenHeight * 2);
                Raylib.EndTextureMode();
                return;
            }
            ReleaseHalfTarget();

            // 3× (a screen too big for 4×): mipmaps average the blocks (trilinear).
            if (factor > 2)
            {
                Raylib.GenTextureMipmaps(ref sceneTarget.Texture);
                Raylib.SetTextureFilter(sceneTarget.Texture, TextureFilter.Trilinear);
            }
        }

        void ReleaseSceneTarget()
        {
            ReleaseHalfTarget();
            if (!sceneTargetReady)
                return;
            Raylib.UnloadRenderTexture(sceneTarget);
            sceneTargetReady = false;
        }

        void ReleaseHalfTarget()
        {
            if (!halfTargetReady)
                return;
            Raylib.UnloadRenderTexture(halfTarget);
            halfTargetReady = false;
        }

        /// <summary>The level: the supersampled texture when anti-aliasing is on, drawn directly otherwise.</summary>
        void DrawWorldAntiAliased()
        {
            if (sceneTargetReady && SupersampleFactor() > 1)
            {
                var texture = sceneScale == 4 && halfTargetReady ? halfTarget.Texture : sceneTarget.Texture;
                DrawTextureCopy(texture, ScreenWidth, ScreenHeight);
                return;
            }
            // Q in the editor: Off draws hard edges (as the game without supersampling), Medium smooth edges, High
            // smooth edges and the best image filtering (scaled-down textures stay sharp without shimmering).
            using (Raylib.PushRenderOptions(new Avalonia.Media.RenderOptions
            {
                EdgeMode = AntiAlias == 0 ? Avalonia.Media.EdgeMode.Aliased : Avalonia.Media.EdgeMode.Antialias,
                BitmapInterpolationMode = AntiAlias switch
                {
                    0 => Avalonia.Media.Imaging.BitmapInterpolationMode.LowQuality, // the game without supersampling: bilinear only
                    1 => Avalonia.Media.Imaging.BitmapInterpolationMode.MediumQuality, // bilinear with mipmaps
                    _ => Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality, // the finest filtering
                },
            }))
            {
                Raylib.BeginMode2D(Camera);
                DrawWorld();
                Raylib.EndMode2D();
            }
        }

        void DrawWorld()
        {

            for (int i = 0; i < GlobalObjects.Drawables.Count; i++)
            {
                GlobalObjects.Drawables[i].Draw();
            }

            // Debug shapes: only those inside the camera's view are drawn.
            if (RenderPlatforms)
            {
                for (int i = 0; i < Sets.Current.Quads.Count; i++)
                {
                    if (IsOnCamera(Sets.Current.Quads[i]))
                        Sets.Current.Quads[i].Draw();
                }
            }
            if (RenderAreas)
            {
                for (int i = 0; i < Sets.Current.Areas.Count; i++)
                {
                    if (IsOnCamera(Sets.Current.Areas[i]))
                        Sets.Current.Areas[i].Draw();
                }
            }
            if (RenderTriggers)
            {
                for (int i = 0; i < Sets.Current.Triggers.Count; i++)
                {
                    if (IsOnCamera(Sets.Current.Triggers[i]))
                        Sets.Current.Triggers[i].DrawRectangle();
                }
            }

            for (int i = 0; i < Models.Model.ModelsAll.Count; i++)
            {
                Models.Model.ModelsAll[i].Draw();
            }

        }

        /// <summary>True when the runner's rectangle (as drawn) overlaps the camera's view.</summary>
        public static bool IsOnCamera(QuadRunner runner)
        {
            if (Sets.Current == null)
                return true;
            var vs = Sets.Current.Containers[1];
            var origin = vs.TransformPoint(runner.rectangle.Origin);
            var size = runner.rectangle.Size * runner.WorldScale;
            var view = Instance.CameraRectangle;
            float minX = MathF.Min(origin.X, origin.X + size.X), maxX = MathF.Max(origin.X, origin.X + size.X);
            float minY = MathF.Min(origin.Y, origin.Y + size.Y), maxY = MathF.Max(origin.Y, origin.Y + size.Y);
            const float margin = 4;
            return maxX >= view.MinX - margin && minX <= view.MaxX + margin && maxY >= view.MinY - margin && minY <= view.MaxY + margin;
        }

        // ================= FULL SCREEN ================= //

        /// <summary>F11: borderless full screen at the monitor's resolution, and back to the window.</summary>
        void ToggleFullScreen()
        {
            // The editor makes the Game View fill its window (or back): the next frame has the new size.
            Raylib.ToggleBorderlessWindowed();
        }

        // ================= DEBUG LABELS ================= //

        // Same colors as the Vectorier editor.
        static readonly Color TriggerOutline = new(0xCE, 0x7D, 0x14, 0xFF);
        static readonly Color AreaOutline = new(0x80, 0x00, 0x00, 0xFF);
        static readonly Color CollisionOutline = new(0x00, 0x00, 0xFF, 0xFF);
        static readonly Color LabelColor = new(0x20, 0x20, 0x20, 0xFF);
        const float LabelSize = 13;
        const float LabelPad = 3;

        /// <summary>
        /// Outline and name of each Trigger, Area, Platform and Trapezoid whose debug view is on, drawn in screen
        /// pixels so they stay sharp and readable at any zoom (the editor's look: 1 px outline, dark label in the
        /// top-left corner, clipped to the shape).
        /// </summary>
        void DrawDebugLabels()
        {
            if (Sets.Current == null)
                return;
            var vs = Sets.Current.Containers[1];

            if (RenderPlatforms)
            {
                for (int i = 0; i < Sets.Current.Quads.Count; i++)
                {
                    var quad = Sets.Current.Quads[i];
                    if (!quad.IsEnabled || quad is AreaRunner || quad is TriggerRunner || !IsOnCamera(quad))
                        continue;
                    if (quad is TrapezoidRunner trapezoid)
                    {
                        var pts = new[] { trapezoid.Point1, trapezoid.Point2, trapezoid.Point3, trapezoid.Point4 }
                            .Select(p => Raylib.GetWorldToScreen2D(vs.TransformPoint(p), Camera)).ToArray();
                        DrawOutline(pts, CollisionOutline);
                        DrawLabel(quad.Name, Bounds(pts));
                        continue;
                    }
                    DrawRunner(quad, vs, CollisionOutline, quad.Name);
                }
            }
            if (RenderAreas)
            {
                for (int i = 0; i < Sets.Current.Areas.Count; i++)
                {
                    var area = Sets.Current.Areas[i];
                    if (!area.IsEnabled || !IsOnCamera(area))
                        continue;
                    string label = area.Name;
                    DrawRunner(area, vs, AreaOutline, label);
                }
            }
            if (RenderTriggers)
            {
                for (int i = 0; i < Sets.Current.Triggers.Count; i++)
                {
                    var trigger = Sets.Current.Triggers[i];
                    if (!trigger.IsEnabled || !IsOnCamera(trigger))
                        continue;
                    DrawRunner(trigger, vs, TriggerOutline, trigger.Name);
                }
            }
        }

        void DrawRunner(QuadRunner runner, Runners.VisualContainer vs, Color outline, string? label)
        {
            var origin = vs.TransformPoint(runner.rectangle.Origin);
            var size = runner.rectangle.Size * runner.WorldScale;
            var a = Raylib.GetWorldToScreen2D(origin, Camera);
            var b = Raylib.GetWorldToScreen2D(origin + size, Camera);
            var r = new Vectorier.Gameplay.Rl.Rectangle(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(b.X - a.X), MathF.Abs(b.Y - a.Y));
            if (r.Width >= 1 && r.Height >= 1)
                Raylib.DrawRectangleLinesEx(new Vectorier.Gameplay.Rl.Rectangle(MathF.Round(r.X), MathF.Round(r.Y), MathF.Round(r.Width), MathF.Round(r.Height)), 1, outline);
            DrawLabel(label, r);
        }

        static void DrawOutline(Vector2[] pts, Color color)
        {
            for (int i = 0; i < pts.Length; i++)
                Raylib.DrawLineEx(pts[i], pts[(i + 1) % pts.Length], 1, color);
        }

        static Vectorier.Gameplay.Rl.Rectangle Bounds(Vector2[] pts)
        {
            float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            return new Vectorier.Gameplay.Rl.Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        static float TextWidth(string text) => GameFont.UiWidth(text, LabelSize);

        static List<string> WrapText(string text, float maxWidth)
        {
            var lines = new List<string>();
            string current = string.Empty;
            foreach (var word in text.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (TextWidth(candidate) <= maxWidth)
                {
                    current = candidate;
                    continue;
                }
                if (current.Length > 0)
                    lines.Add(current);
                // A single word wider than the shape: split it by characters.
                current = string.Empty;
                foreach (char c in word)
                {
                    if (current.Length > 0 && TextWidth(current + c) > maxWidth)
                    {
                        lines.Add(current);
                        current = string.Empty;
                    }
                    current += c;
                }
            }
            if (current.Length > 0)
                lines.Add(current);
            return lines;
        }

        static void DrawLabel(string? text, Vectorier.Gameplay.Rl.Rectangle r)
        {
            if (string.IsNullOrEmpty(text) || r.Width < 14 || r.Height < 10)
                return;
            float x = r.X + LabelPad, y = r.Y + LabelPad;
            float w = r.Width - LabelPad * 2, h = r.Height - LabelPad * 2;
            if (w <= 0 || h <= 0)
                return;
            // Off screen: nothing to draw (the scissor would clamp it to the screen edge).
            if (x > Raylib.GetScreenWidth() || y > Raylib.GetScreenHeight() || x + w < 0 || y + h < 0)
                return;
            Raylib.BeginScissorMode((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Ceiling(w), (int)MathF.Ceiling(h));
            // Wrapped to the shape's width (like the editor): words move to the next line, a word longer than the
            // width is split; only lines below the shape's bottom are cut.
            float lineY = MathF.Round(y);
            foreach (var line in WrapText(text, w))
            {
                if (lineY > y + h)
                    break;
                GameFont.DrawUi(line, MathF.Round(x), lineY, LabelSize, LabelColor);
                lineY += LabelSize + 2;
            }
            Raylib.EndScissorMode();
        }

        void SaveSettings()
        {
            Settings = $"RenderPlatforms={RenderPlatforms}\n" +
                       $"RenderTriggers={RenderTriggers}\n" +
                       $"RenderAreas={RenderAreas}\n" +
                       $"NodeMode={(int)NodeMode}\n" +
                       $"ShowGrid={ShowGrid}\n" +
                       $"MovesInfo={(int)MovesInfo}\n" +
                       $"MusicMuted={SoundsManager.MusicMuted}\n" +
                       $"RenderEdges={RenderEdges}\n" +
                       $"RenderDetectors={RenderDetectors}\n" +
                       $"RenderBoxes={RenderBoxes}\n" +
                       $"AntiAlias={AntiAlias}";
            SaveSettingsText?.Invoke(Settings);
        }

        void LoadSettings()
        {
            if (LoadSettingsText?.Invoke() is string text)
            {
                string[] lines = text.Split('\n');
                foreach (string line in lines)
                {
                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();
                        bool boolValue = value.ToLower() == "true";
                        switch (key)
                        {
                            case "RenderPlatforms":
                                RenderPlatforms = boolValue;
                                break;
                            case "RenderTriggers":
                                RenderTriggers = boolValue;
                                break;
                            case "RenderAreas":
                                RenderAreas = boolValue;
                                break;
                            case "RenderNodes": // older settings files: on = all nodes
                                NodeMode = boolValue ? NodeDisplay.All : NodeDisplay.None;
                                break;
                            case "NodeMode":
                                if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int nodeMode))
                                    NodeMode = (NodeDisplay)Math.Clamp(nodeMode, 0, NodeModeNames.Length - 1);
                                break;
                            case "ShowGrid":
                                ShowGrid = boolValue;
                                break;
                            case "MovesInfo":
                                if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int movesInfo))
                                    MovesInfo = (MovesInfoMode)Math.Clamp(movesInfo, 0, MovesInfoNames.Length - 1);
                                break;
                            case "MusicMuted":
                                SoundsManager.MusicMuted = boolValue;
                                break;
                            case "RenderEdges":
                                RenderEdges = boolValue;
                                break;
                            case "RenderDetectors":
                                RenderDetectors = boolValue;
                                break;
                            case "RenderBoxes":
                                RenderBoxes = boolValue;
                                break;
                            case "AntiAlias":
                                if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int aa))
                                    AntiAlias = Math.Clamp(aa, 0, 2);
                                break;
                        }
                    }
                }
            }
        }

        void UpdateButtons()
        {
            foreach (var button in Buttons.ToList()) // Full Screen rebuilds the panel while it is clicked
                button.Update();
        }

        void DrawButtons()
        {
            foreach (var button in Buttons)
                button.Draw();
        }
    }
}
