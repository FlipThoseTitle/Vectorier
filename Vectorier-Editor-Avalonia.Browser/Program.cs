using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Vectorier_Editor_Avalonia;
using Vectorier_Editor_Avalonia.Browser;
using Vectorier_Editor_Avalonia.Services;


internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        await JSHost.ImportAsync("vectorier", "../vectorier.js");
        AppServices.Platform = new BrowserPlatformServices();
        await BuildAvaloniaApp()
            .WithInterFont()
            // Keep the level's textures on the GPU (Skia's default cache is ~28 MB).
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = 512L * 1024 * 1024 })
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
