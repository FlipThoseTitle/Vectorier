using System;
using Avalonia;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.Platform;

namespace Vectorier_Editor_Avalonia.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppServices.Platform = new DesktopPlatformServices(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // Skia's GPU texture cache defaults to ~28 MB: a level's textures don't fit, so they were
            // uploaded to the GPU again every frame (heavy stutter in the Scene). Keep them resident.
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = 1024L * 1024 * 1024 })
            .LogToTrace();
}
