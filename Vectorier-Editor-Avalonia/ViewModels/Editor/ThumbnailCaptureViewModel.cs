using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier_Editor_Avalonia.Controls.Scene;
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.Projects;
using Vectorier_Editor_Avalonia.ViewModels.Dialogs;

namespace Vectorier_Editor_Avalonia.ViewModels.Editor;

/// <summary>
/// Captures the level thumbnail from the Scene: a 512 × 340 frame (the Camera's view height) that the user pans
/// around; only Objects and Images are drawn. Apply renders it to icons/stories/&lt;level&gt;.png.
/// </summary>
public sealed partial class ThumbnailCaptureViewModel : DialogViewModel
{
    public ThumbnailCaptureViewModel(EditorViewModel editor)
    {
        Editor = editor;
        Title = $"Capture Thumbnail - {editor.Document.LevelName}";
        var camera = editor.Document.Roots.SelectMany(SceneDocument.Walk).OfType<CameraElement>().FirstOrDefault();
        if (camera != null)
        {
            var p = camera.ParentWorldPosition;
            _center = new Point(p.X + camera.X, p.Y + camera.Y);
        }
        else
        {
            _center = editor.SceneViewCenter;
        }
    }

    public EditorViewModel Editor { get; }

    public override bool Resizable => true;
    public override double PreferredWidth => 900;
    public override double PreferredHeight => 680;

    public static double Aspect => (double)LevelThumbnail.Width / LevelThumbnail.Height;

    /// <summary>Level point at the middle of the frame.</summary>
    [ObservableProperty] private Point _center;

    /// <summary>Level units covered by the frame's height (the Camera shows 800).</summary>
    [ObservableProperty] private double _frameHeight = CameraElement.ViewSize.Height;

    [ObservableProperty] private bool _isBusy;

    public event Action? Changed;

    partial void OnCenterChanged(Point value) => Changed?.Invoke();

    partial void OnFrameHeightChanged(double value)
    {
        double clamped = Math.Clamp(value, 100, 20000);
        if (Math.Abs(clamped - value) > 1e-9)
        {
            FrameHeight = clamped;
            return;
        }
        Changed?.Invoke();
    }

    [RelayCommand]
    private void ResetZoom() => FrameHeight = CameraElement.ViewSize.Height;

    /// <summary>Draws the frame at the thumbnail's resolution.</summary>
    public RenderTargetBitmap Render()
    {
        var size = new Size(LevelThumbnail.Width, LevelThumbnail.Height);
        var bitmap = new RenderTargetBitmap(new PixelSize(LevelThumbnail.Width, LevelThumbnail.Height));
        var renderer = new SceneRenderer(Editor);
        using (var ctx = bitmap.CreateDrawingContext(true))
        {
            ctx.FillRectangle(Brushes.White, new Rect(size));
            renderer.Render(ctx, new SceneViewport(Center, size.Height / FrameHeight, size), SceneRenderMode.Thumbnail);
        }
        return bitmap;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        try
        {
            IsBusy = true;
            using var bitmap = Render();
            using var stream = new MemoryStream();
            bitmap.Save(stream, new PngBitmapEncoderOptions());
            await new LevelThumbnail(Editor.Project, Editor.Document.LevelName).SavePngAsync(stream.ToArray());
            Close(true);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Capture Thumbnail", ex.Message, MessageKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelCapture() => Close(null);
}
