// The editor's stand-in for Raylib-cs: what the game calls, done with Avalonia's drawing on the Game View, the
// editor's bitmaps and the game audio mixer. One frame is drawn between BeginDrawing and EndDrawing into the
// DrawingContext the Game View hands over (see Vectorier.Gameplay.Host.GameHost).
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Vectorier.Gameplay.Audio;
using AColor = Avalonia.Media.Color;
using AMatrix = Avalonia.Matrix;
using APoint = Avalonia.Point;

namespace Vectorier.Gameplay.Rl
{
    public static class Raylib
    {
        public const float DEG2RAD = MathF.PI / 180f;
        public const float RAD2DEG = 180f / MathF.PI;

        // ================= FRAME ================= //

        private static DrawingContext _ctx;
        // Raylib's 2D mode and scissor are independent switches; Avalonia's pushed states must be undone in order.
        // Both live on one stack: ending one that is not on top undoes the ones above it and pushes them again.
        private sealed class PushedState
        {
            public bool IsMode;
            public bool IsOptions;
            public Func<DrawingContext, DrawingContext.PushedState> Push;
            public DrawingContext.PushedState Pushed;
        }

        private static readonly List<PushedState> _pushed = new();

        private static void PushState(bool isMode, Func<DrawingContext, DrawingContext.PushedState> push)
        {
            if (_ctx == null) return;
            _pushed.Add(new PushedState { IsMode = isMode, Push = push, Pushed = push(_ctx) });
        }

        private static void PopState(bool isMode) =>
            PopAt(_pushed.FindLastIndex(p => p.IsMode == isMode && p.Push != null && !p.IsOptions));

        private static void PopAt(int index)
        {
            if (index < 0 || _ctx == null) return;
            for (int i = _pushed.Count - 1; i >= index; i--)
                _pushed[i].Pushed.Dispose();
            _pushed.RemoveAt(index);
            for (int i = index; i < _pushed.Count; i++)
                _pushed[i].Pushed = _pushed[i].Push(_ctx);
        }

        /// <summary>Undoes every state the game pushed this frame (its end, or a frame cut short by an error).</summary>
        public static void PopAllStates()
        {
            for (int i = _pushed.Count - 1; i >= 0; i--)
            {
                try { _pushed[i].Pushed.Dispose(); } catch (InvalidOperationException) { }
            }
            _pushed.Clear();
        }
        private static readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>The Game View's size in pixels (the game's window).</summary>
        public static int ScreenWidth { get; private set; } = 1280;
        public static int ScreenHeight { get; private set; } = 720;

        /// <summary>Set by the host before a frame: where to draw and how big the "window" is.</summary>
        public static void SetTarget(DrawingContext context, int width, int height)
        {
            _ctx = context;
            ScreenWidth = Math.Max(1, width);
            ScreenHeight = Math.Max(1, height);
        }

        public static bool IsDrawing => _ctx != null;

        /// <summary>The editor's own: drawing options for a part of the frame (disposed in order, around a 2D mode).</summary>
        public static IDisposable PushRenderOptions(Avalonia.Media.RenderOptions options)
        {
            if (_ctx == null) return new OptionsState(null);
            var state = new PushedState { IsOptions = true, Push = c => c.PushRenderOptions(options) };
            state.Pushed = state.Push(_ctx);
            _pushed.Add(state);
            return new OptionsState(state);
        }

        // Undone wherever it is on the stack (a 2D mode left on by an error is above it).
        private sealed class OptionsState(PushedState state) : IDisposable
        {
            public void Dispose() => PopAt(state == null ? -1 : _pushed.IndexOf(state));
        }

        public static void BeginDrawing() { }

        public static void EndDrawing()
        {
            PopAllStates();
            _camera = null;
        }

        public static void ClearBackground(Color color)
        {
            _ctx?.FillRectangle(Brush(color), new Rect(0, 0, ScreenWidth, ScreenHeight));
        }

        // ================= WINDOW (the Game View) ================= //

        public static void InitWindow(int width, int height, string title) { }
        public static void CloseWindow() { }
        public static bool WindowShouldClose() => false;
        public static void SetTargetFPS(int fps) { }
        public static void SetTraceLogLevel(TraceLogLevel level) { }
        public static void SetExitKey(KeyboardKey key) { }
        public static void SetConfigFlags(ConfigFlags flags) { }
        public static void ToggleBorderlessWindowed() => FullScreenRequested?.Invoke();
        public static bool IsWindowState(ConfigFlags flag) => flag == ConfigFlags.BorderlessWindowMode && IsFullScreen?.Invoke() == true;

        /// <summary>The game's Full Screen toggle (F11): the host makes the Game View fill the editor's window.</summary>
        public static event Action FullScreenRequested;
        public static Func<bool> IsFullScreen;

        public static int GetScreenWidth() => ScreenWidth;
        public static int GetScreenHeight() => ScreenHeight;
        public static int GetRenderWidth() => ScreenWidth;
        public static int GetRenderHeight() => ScreenHeight;
        public static Vector2 GetScreenCenter() => new(ScreenWidth / 2f, ScreenHeight / 2f);

        // ================= TIME ================= //

        private static float _frameTime = 1f / 60;
        private static int _fps = 60;

        /// <summary>Set by the host after each frame.</summary>
        public static void SetFrameTiming(float frameTime, int fps)
        {
            _frameTime = frameTime;
            _fps = fps;
        }

        public static double GetTime() => _clock.Elapsed.TotalSeconds;
        public static float GetFrameTime() => _frameTime;
        public static int GetFPS() => _fps;

        // ================= INPUT ================= //

        private static readonly HashSet<KeyboardKey> _down = new();
        private static readonly HashSet<KeyboardKey> _pressed = new();
        private static readonly HashSet<KeyboardKey> _pendingPressed = new();
        private static readonly HashSet<MouseButton> _mouseDown = new();
        private static Vector2 _mouse;

        /// <summary>A key went down on the Game View (repeats are ignored).</summary>
        public static void KeyDown(KeyboardKey key)
        {
            if (key == KeyboardKey.Null) return;
            if (_down.Add(key)) _pendingPressed.Add(key);
        }

        public static void KeyUp(KeyboardKey key) => _down.Remove(key);

        public static void MouseMoved(Vector2 position) => _mouse = position;

        public static void MouseButtonChanged(MouseButton button, bool down)
        {
            if (down) _mouseDown.Add(button); else _mouseDown.Remove(button);
        }

        /// <summary>Raylib's PollInputEvents: the presses since the last frame become this frame's.</summary>
        public static void PollInputEvents()
        {
            _pressed.Clear();
            foreach (var key in _pendingPressed) _pressed.Add(key);
            _pendingPressed.Clear();
        }

        /// <summary>The Game View lost the keyboard: nothing is held any more.</summary>
        public static void ReleaseAllKeys()
        {
            _down.Clear();
            _pendingPressed.Clear();
            _mouseDown.Clear();
        }

        public static bool IsKeyPressed(KeyboardKey key) => _pressed.Contains(key);
        public static bool IsKeyDown(KeyboardKey key) => _down.Contains(key);
        public static bool IsKeyUp(KeyboardKey key) => !_down.Contains(key);
        public static bool IsKeyReleased(KeyboardKey key) => false;
        public static bool IsMouseButtonDown(MouseButton button) => _mouseDown.Contains(button);
        public static Vector2 GetMousePosition() => _mouse;

        // ================= 2D CAMERA ================= //

        private static Camera2D? _camera;

        private static AMatrix CameraMatrix(Camera2D camera) =>
            AMatrix.CreateTranslation(-camera.Target.X, -camera.Target.Y)
            * AMatrix.CreateRotation(camera.Rotation * DEG2RAD)
            * AMatrix.CreateScale(camera.Zoom, camera.Zoom)
            * AMatrix.CreateTranslation(camera.Offset.X, camera.Offset.Y);

        public static void BeginMode2D(Camera2D camera)
        {
            _camera = camera;
            var matrix = CameraMatrix(camera);
            PushState(true, c => c.PushTransform(matrix));
        }

        public static void EndMode2D()
        {
            _camera = null;
            PopState(true);
        }

        public static Vector2 GetWorldToScreen2D(Vector2 position, Camera2D camera)
        {
            var p = new APoint(position.X, position.Y).Transform(CameraMatrix(camera));
            return new Vector2((float)p.X, (float)p.Y);
        }

        public static Vector2 GetScreenToWorld2D(Vector2 position, Camera2D camera)
        {
            var m = CameraMatrix(camera);
            if (!m.TryInvert(out var inverse))
                return position;
            var p = new APoint(position.X, position.Y).Transform(inverse);
            return new Vector2((float)p.X, (float)p.Y);
        }

        // ================= RENDER TEXTURES, BLENDING, SCISSOR ================= //

        // The game's supersampled anti-aliasing draws the level into a bigger texture: the editor's drawing is
        // anti-aliased already, so a render texture draws straight to the view.
        public static RenderTexture2D LoadRenderTexture(int width, int height) =>
            new() { Id = 0, Texture = new Texture2D { Id = 0, Width = width, Height = height } };
        public static void UnloadRenderTexture(RenderTexture2D target) { }
        public static void BeginTextureMode(RenderTexture2D target) { }
        public static void EndTextureMode() { }
        public static void GenTextureMipmaps(ref Texture2D texture) { }
        public static void BeginBlendMode(BlendMode mode) { }
        public static void EndBlendMode() { }

        public static void BeginScissorMode(int x, int y, int width, int height)
        {
            // A scissor is in window pixels, whatever 2D mode is on.
            var rect = new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
            PushState(false, c => c.PushClip(rect));
        }

        public static void EndScissorMode()
        {
            PopState(false);
        }

        // ================= SHAPES ================= //

        private static readonly Dictionary<uint, IImmutableSolidColorBrush> _brushes = new();
        private static readonly Dictionary<(uint, float), IPen> _pens = new();

        internal static IBrush Brush(Color color)
        {
            uint key = color.Argb;
            if (!_brushes.TryGetValue(key, out var brush))
            {
                if (_brushes.Count > 4096) _brushes.Clear();
                _brushes[key] = brush = new ImmutableSolidColorBrush(AColor.FromUInt32(key));
            }
            return brush;
        }

        private static IPen Pen(Color color, float thickness, PenLineCap cap = PenLineCap.Flat)
        {
            var key = (color.Argb, thickness + (cap == PenLineCap.Round ? 100000 : 0));
            if (!_pens.TryGetValue(key, out var pen))
            {
                if (_pens.Count > 4096) _pens.Clear();
                _pens[key] = pen = new ImmutablePen(new ImmutableSolidColorBrush(AColor.FromUInt32(color.Argb)), thickness, lineCap: cap);
            }
            return pen;
        }

        private static APoint P(Vector2 v) => new(v.X, v.Y);

        public static void DrawCircleV(Vector2 center, float radius, Color color)
        {
            if (_ctx == null || !(radius > 0) || color.A == 0) return;
            _ctx.DrawEllipse(Brush(color), null, P(center), radius, radius);
        }

        public static void DrawCircle(int x, int y, float radius, Color color) => DrawCircleV(new Vector2(x, y), radius, color);

        public static void DrawCircleLinesV(Vector2 center, float radius, Color color)
        {
            if (_ctx == null || !(radius > 0) || color.A == 0) return;
            _ctx.DrawEllipse(null, Pen(color, 1), P(center), radius, radius);
        }

        public static void DrawRing(Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle, int segments, Color color)
        {
            if (_ctx == null || color.A == 0 || outerRadius <= innerRadius) return;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                int n = Math.Max(4, segments);
                float step = (endAngle - startAngle) / n;
                Vector2 At(float r, float deg) => center + new Vector2(MathF.Cos(deg * DEG2RAD), MathF.Sin(deg * DEG2RAD)) * r;
                g.BeginFigure(P(At(outerRadius, startAngle)), true);
                for (int i = 1; i <= n; i++) g.LineTo(P(At(outerRadius, startAngle + step * i)));
                for (int i = n; i >= 0; i--) g.LineTo(P(At(innerRadius, startAngle + step * i)));
                g.EndFigure(true);
            }
            _ctx.DrawGeometry(Brush(color), null, geometry);
        }

        public static void DrawLineEx(Vector2 start, Vector2 end, float thick, Color color)
        {
            if (_ctx == null || color.A == 0 || !(thick > 0)) return;
            _ctx.DrawLine(Pen(color, thick), P(start), P(end));
        }

        public static void DrawLineV(Vector2 start, Vector2 end, Color color) => DrawLineEx(start, end, 1, color);

        public static void DrawLine(int x1, int y1, int x2, int y2, Color color) => DrawLineEx(new Vector2(x1, y1), new Vector2(x2, y2), 1, color);

        public static void DrawRectangleV(Vector2 position, Vector2 size, Color color) => DrawRectangleRec(new Rectangle(position, size), color);

        public static void DrawRectangle(int x, int y, int width, int height, Color color) => DrawRectangleRec(new Rectangle(x, y, width, height), color);

        public static void DrawRectangleRec(Rectangle rec, Color color)
        {
            if (_ctx == null || color.A == 0) return;
            _ctx.FillRectangle(Brush(color), Normalize(rec));
        }

        public static void DrawRectangleLinesEx(Rectangle rec, float lineThick, Color color)
        {
            if (_ctx == null || color.A == 0) return;
            var r = Normalize(rec);
            // Raylib draws the lines inside the rectangle.
            double t = lineThick / 2.0;
            var inner = new Rect(r.X + t, r.Y + t, Math.Max(0, r.Width - lineThick), Math.Max(0, r.Height - lineThick));
            _ctx.DrawRectangle(null, Pen(color, lineThick), inner);
        }

        public static void DrawRectangleRounded(Rectangle rec, float roundness, int segments, Color color)
        {
            if (_ctx == null || color.A == 0) return;
            var r = Normalize(rec);
            double radius = Math.Min(r.Width, r.Height) * Math.Clamp(roundness, 0, 1) / 2;
            _ctx.DrawRectangle(Brush(color), null, r, radius, radius);
        }

        public static void DrawTriangle(Vector2 v1, Vector2 v2, Vector2 v3, Color color)
        {
            if (_ctx == null || color.A == 0) return;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(P(v1), true);
                g.LineTo(P(v2));
                g.LineTo(P(v3));
                g.EndFigure(true);
            }
            _ctx.DrawGeometry(Brush(color), null, geometry);
        }

        private static Rect Normalize(Rectangle rec)
        {
            double x = rec.Width < 0 ? rec.X + rec.Width : rec.X;
            double y = rec.Height < 0 ? rec.Y + rec.Height : rec.Y;
            return new Rect(x, y, Math.Abs(rec.Width), Math.Abs(rec.Height));
        }

        public static bool CheckCollisionRecs(Rectangle a, Rectangle b) =>
            a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

        public static bool CheckCollisionPointRec(Vector2 point, Rectangle rec) =>
            point.X >= rec.X && point.X < rec.X + rec.Width && point.Y >= rec.Y && point.Y < rec.Y + rec.Height;

        public static Color Fade(Color color, float alpha) => new(color.R, color.G, color.B, (byte)(255 * Math.Clamp(alpha, 0, 1)));

        public static Color ColorAlpha(Color color, float alpha) => Fade(color, alpha);

        // ================= TEXTURES ================= //

        private static readonly List<IImage> _textures = new() { null };

        /// <summary>Makes a texture of an editor image (project texture, atlas, built-in picture).</summary>
        public static Texture2D RegisterTexture(IImage image)
        {
            if (image == null)
                return default;
            _textures.Add(image);
            var size = image.Size;
            if (image is Bitmap bitmap)
                size = new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            return new Texture2D { Id = (uint)(_textures.Count - 1), Width = (int)size.Width, Height = (int)size.Height, Mipmaps = 1 };
        }

        /// <summary>A texture whose image comes later (read while the game runs): nothing is drawn with it until then.</summary>
        public static Texture2D RegisterPendingTexture()
        {
            _textures.Add(null);
            return new Texture2D { Id = (uint)(_textures.Count - 1), Mipmaps = 1 };
        }

        public static void SetTextureImage(uint id, IImage image)
        {
            if (id > 0 && id < _textures.Count)
                _textures[(int)id] = image;
        }

        /// <summary>Changes when the handles go (ResetTextures): a texture kept from before must be registered again.</summary>
        public static int TextureGeneration { get; private set; }

        internal static IImage TextureImage(uint id) => id > 0 && id < _textures.Count ? _textures[(int)id] : null;

        /// <summary>The level was left: its textures are the editor's (cached there), only the handles go.</summary>
        public static void ResetTextures()
        {
            TextureGeneration++;
            _textures.Clear();
            _textures.Add(null);
        }

        public static Image LoadImageFromMemory(string fileType, byte[] data)
        {
            try
            {
                var bitmap = new Bitmap(new System.IO.MemoryStream(data));
                return new Image { Width = bitmap.PixelSize.Width, Height = bitmap.PixelSize.Height, Mipmaps = 1, Bitmap = bitmap };
            }
            catch
            {
                return default;
            }
        }

        public static Texture2D LoadTextureFromImage(Image image) => image.Bitmap == null ? default : RegisterTexture(image.Bitmap);

        public static void UnloadImage(Image image) { }
        public static void UnloadTexture(Texture2D texture) { }
        public static void SetTextureFilter(Texture2D texture, TextureFilter filter) { }
        public static void SetTextureWrap(Texture2D texture, TextureWrap wrap) { }

        public static void DrawTexturePro(Texture2D texture, Rectangle source, Rectangle dest, Vector2 origin, float rotation, Color tint)
        {
            if (_ctx == null || tint.A == 0 || texture.Image is not { } image) return;
            // Raylib: a negative source width / height flips the image.
            bool flipX = source.Width < 0, flipY = source.Height < 0;
            var src = new Rect(flipX ? source.X + source.Width : source.X, flipY ? source.Y + source.Height : source.Y,
                Math.Abs(source.Width), Math.Abs(source.Height));
            if (src.Width <= 0 || src.Height <= 0 || dest.Width == 0 || dest.Height == 0) return;
            var m = AMatrix.CreateScale(flipX ? -1 : 1, flipY ? -1 : 1)
                    * AMatrix.CreateTranslation(flipX ? dest.Width : 0, flipY ? dest.Height : 0)
                    * AMatrix.CreateTranslation(-origin.X, -origin.Y)
                    * AMatrix.CreateRotation(rotation * DEG2RAD)
                    * AMatrix.CreateTranslation(dest.X, dest.Y);
            using (_ctx.PushTransform(m))
                DrawImage(image, src, new Rect(0, 0, dest.Width, dest.Height), tint);
        }

        public static void DrawTexture(Texture2D texture, int x, int y, Color tint) =>
            DrawTexturePro(texture, new Rectangle(0, 0, texture.Width, texture.Height), new Rectangle(x, y, texture.Width, texture.Height), Vector2.Zero, 0, tint);

        public static void DrawTextureV(Texture2D texture, Vector2 position, Color tint) => DrawTexture(texture, (int)position.X, (int)position.Y, tint);

        /// <summary>An image with Raylib's tint (RGB multiplies the colours, alpha is opacity).</summary>
        internal static void DrawImage(IImage image, Rect source, Rect dest, Color tint)
        {
            var drawn = image;
            if ((tint.R != 255 || tint.G != 255 || tint.B != 255) && image is Bitmap bitmap)
            {
                // Quantized: colour transformations change the tint every frame.
                var rgb = AColor.FromRgb((byte)(tint.R & 0xFC | tint.R >> 6), (byte)(tint.G & 0xFC | tint.G >> 6), (byte)(tint.B & 0xFC | tint.B >> 6));
                drawn = Vectorier.Controls.Scene.TintCache.Get(bitmap, rgb) ?? image;
            }
            if (tint.A < 255)
            {
                using (_ctx.PushOpacity(tint.A / 255.0))
                    _ctx.DrawImage(drawn, source, dest);
            }
            else
            {
                _ctx.DrawImage(drawn, source, dest);
            }
        }

        internal static DrawingContext Context => _ctx;

        // ================= TEXT ================= //


        public static Font GetFontDefault() => new() { BaseSize = 20, Typeface = new Typeface(FontFamily.Default) };

        /// <summary>A font of the editor (the game's Play, built into the editor's assets).</summary>
        public static Font LoadFont(Typeface typeface, int baseSize) => new() { BaseSize = baseSize, Typeface = typeface };

        // Laying out a text (shaping) is the costly part of drawing it: the same texts are drawn every frame (the
        // buttons, the move's name), so the laid-out ones are kept (the oldest dropped when there are many).
        private readonly record struct TextKey(Typeface Typeface, string Text, float Size, uint Color);
        private static readonly Dictionary<TextKey, FormattedText> _texts = new();

        private static FormattedText Text(Font font, string text, float size, Color color)
        {
            var typeface = font.Typeface ?? new Typeface(FontFamily.Default);
            var key = new TextKey(typeface, text ?? string.Empty, Math.Max(1, size), Pack(color));
            if (_texts.TryGetValue(key, out var cached))
                return cached;
            if (_texts.Count >= 1024)
                _texts.Clear();
            var formatted = new FormattedText(key.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, key.Size, Brush(color));
            _texts[key] = formatted;
            return formatted;
        }

        private static uint Pack(Color c) => (uint)(c.R << 24 | c.G << 16 | c.B << 8 | c.A);

        public static void DrawTextEx(Font font, string text, Vector2 position, float fontSize, float spacing, Color tint)
        {
            if (_ctx == null || string.IsNullOrEmpty(text) || tint.A == 0) return;
            _ctx.DrawText(Text(font, text, fontSize, tint), P(position));
        }

        public static void DrawText(string text, int x, int y, int fontSize, Color color) =>
            DrawTextEx(GetFontDefault(), text, new Vector2(x, y), fontSize, 1, color);

        public static Vector2 MeasureTextEx(Font font, string text, float fontSize, float spacing)
        {
            if (string.IsNullOrEmpty(text)) return new Vector2(0, fontSize);
            var formatted = Text(font, text, fontSize, Color.White);
            return new Vector2((float)formatted.WidthIncludingTrailingWhitespace, (float)Math.Max(fontSize, formatted.Height));
        }

        public static int MeasureText(string text, int fontSize) => (int)MeasureTextEx(GetFontDefault(), text, fontSize, 1).X;

        // ================= AUDIO (the game mixer) ================= //

        public static void InitAudioDevice() => GameAudio.Start();
        public static void CloseAudioDevice() => GameAudio.Stop();
        public static bool IsAudioDeviceReady() => GameAudio.IsRunning;
        public static void SetMasterVolume(float volume) => GameAudio.Mixer.MasterVolume = volume;

        public static Sound LoadSoundFromData(SoundData data) => data == null ? default : new Sound { Data = data, FrameCount = (uint)data.FrameCount };
        public static void UnloadSound(Sound sound) { }
        public static void PlaySound(Sound sound) { if (sound.Data != null) GameAudio.Mixer.Play(sound.Data); }
        public static void StopSound(Sound sound) { if (sound.Data != null) GameAudio.Mixer.Stop(sound.Data); }
        public static bool IsSoundPlaying(Sound sound) => sound.Data != null && GameAudio.Mixer.IsPlaying(sound.Data);
        public static void SetSoundVolume(Sound sound, float volume) { if (sound.Data != null) sound.Data.Volume = Math.Clamp(volume, 0, 1); }

        public static Music LoadMusicFromData(SoundData data) => data == null ? default : new Music { Data = data, FrameCount = (uint)data.FrameCount, Looping = true };
        public static void UnloadMusicStream(Music music) { if (music.Data != null) GameAudio.Mixer.StopMusic(music.Data); }
        public static void PlayMusicStream(Music music) { if (music.Data != null) GameAudio.Mixer.PlayMusic(music.Data, music.Looping); }
        public static void StopMusicStream(Music music) { if (music.Data != null) GameAudio.Mixer.StopMusic(music.Data); }
        public static void PauseMusicStream(Music music) { if (music.Data != null) GameAudio.Mixer.PauseMusic(music.Data, true); }
        public static void ResumeMusicStream(Music music) { if (music.Data != null) GameAudio.Mixer.PauseMusic(music.Data, false); }
        public static void UpdateMusicStream(Music music) { }
        public static bool IsMusicStreamPlaying(Music music) => music.Data != null && GameAudio.Mixer.IsMusicPlaying(music.Data);
        public static void SetMusicVolume(Music music, float volume) { if (music.Data != null) music.Data.Volume = Math.Clamp(volume, 0, 1); }
    }

    /// <summary>Raylib's rlgl immediate mode: the game draws textured quads (affine images) and triangles.</summary>
    public static class Rlgl
    {
        private static DrawMode _mode;
        private static uint _texture;
        private static Color _color = Color.White;
        private static readonly List<(Vector2 Position, Vector2 Uv, Color Color)> _vertices = new();
        private static Vector2 _uv;

        public static void DisableBackfaceCulling() { }
        public static void EnableBackfaceCulling() { }
        public static void SetBlendFactors(int src, int dst, int equation) { }
        public static void SetTexture(uint id) => _texture = id;
        public static void Begin(DrawMode mode)
        {
            _mode = mode;
            _vertices.Clear();
        }
        public static void Color4ub(byte r, byte g, byte b, byte a) => _color = new Color(r, g, b, a);
        public static void TexCoord2f(float u, float v) => _uv = new Vector2(u, v);
        public static void Vertex2f(float x, float y) => _vertices.Add((new Vector2(x, y), _uv, _color));

        public static void End()
        {
            var ctx = Raylib.Context;
            if (ctx == null)
            {
                _vertices.Clear();
                return;
            }
            int per = _mode == DrawMode.Quads ? 4 : _mode == DrawMode.Triangles ? 3 : 2;
            for (int i = 0; i + per <= _vertices.Count; i += per)
            {
                if (_mode == DrawMode.Quads && Raylib.TextureImage(_texture) is { } image)
                    DrawTexturedQuad(ctx, image, i);
                else if (_mode != DrawMode.Lines)
                    FillPolygon(ctx, i, per);
                else
                    Raylib.DrawLineV(_vertices[i].Position, _vertices[i + 1].Position, _vertices[i].Color);
            }
            _vertices.Clear();
        }

        /// <summary>
        /// A textured quad from the game's image drawing is a parallelogram (an affine transform of the texture):
        /// the image is drawn under the matrix mapping its texture corners onto the quad's first three corners.
        /// </summary>
        private static void DrawTexturedQuad(DrawingContext ctx, IImage image, int i)
        {
            var (p0, t0, c0) = _vertices[i];
            var (p1, t1, _) = _vertices[i + 1];
            var (p3, t3, _) = _vertices[i + 3];
            var size = image is Bitmap b ? new Size(b.PixelSize.Width, b.PixelSize.Height) : image.Size;
            // The affine map from texture space (u, v in 0..1) to the screen, from three corners (works for the
            // atlas' rotated sprites too, where u runs along the quad's other side).
            double a1 = t1.X - t0.X, b1 = t3.X - t0.X, c1 = t1.Y - t0.Y, d1 = t3.Y - t0.Y;
            double det = a1 * d1 - b1 * c1;
            if (Math.Abs(det) < 1e-12) return;
            double ia = d1 / det, ib = -b1 / det, ic = -c1 / det, id = a1 / det;
            var e1 = p1 - p0; var e3 = p3 - p0;
            double mxx = e1.X * ia + e3.X * ic, mxy = e1.X * ib + e3.X * id;
            double myx = e1.Y * ia + e3.Y * ic, myy = e1.Y * ib + e3.Y * id;
            double ox = p0.X - (mxx * t0.X + mxy * t0.Y), oy = p0.Y - (myx * t0.X + myy * t0.Y);
            var m = new AMatrix(mxx, myx, mxy, myy, ox, oy);
            // Only the quad's part of the texture (one sprite of an atlas, not the whole sheet).
            var t2 = _vertices[i + 2].Uv;
            double u0 = Math.Min(Math.Min(t0.X, t1.X), Math.Min(t2.X, t3.X)), u1 = Math.Max(Math.Max(t0.X, t1.X), Math.Max(t2.X, t3.X));
            double v0 = Math.Min(Math.Min(t0.Y, t1.Y), Math.Min(t2.Y, t3.Y)), v1 = Math.Max(Math.Max(t0.Y, t1.Y), Math.Max(t2.Y, t3.Y));
            if (u1 - u0 <= 0 || v1 - v0 <= 0) return;
            var source = new Rect(u0 * size.Width, v0 * size.Height, (u1 - u0) * size.Width, (v1 - v0) * size.Height);
            using (ctx.PushTransform(m))
                Raylib.DrawImage(image, source, new Rect(u0, v0, u1 - u0, v1 - v0), c0);
        }

        private static void FillPolygon(DrawingContext ctx, int i, int count)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new APoint(_vertices[i].Position.X, _vertices[i].Position.Y), true);
                for (int k = 1; k < count; k++)
                    g.LineTo(new APoint(_vertices[i + k].Position.X, _vertices[i + k].Position.Y));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(Raylib.Brush(_vertices[i].Color), null, geometry);
        }
    }
}
