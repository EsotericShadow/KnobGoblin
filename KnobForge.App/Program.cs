using Avalonia;
using Avalonia.Native;
using System;
using System.IO;

namespace KnobForge.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

        string requestedMode = (Environment.GetEnvironmentVariable("KNOBFORGE_RENDER_MODE") ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        string mode = string.IsNullOrWhiteSpace(requestedMode) ? "metal" : requestedMode;
        if (OperatingSystem.IsMacOS() && (mode == "software" || mode == "opengl"))
        {
            Console.Error.WriteLine($">>> RenderMode='{mode}' requested but overridden to 'metal' (GPU-only policy).");
            mode = "metal";
        }

        Console.WriteLine($">>> RenderMode={mode}");

        var appBuilder = BuildAvaloniaApp();
        if (OperatingSystem.IsMacOS())
        {
            AvaloniaNativePlatformOptions options = mode switch
            {
                "metal" => new AvaloniaNativePlatformOptions
                {
                    RenderingMode = new[] { AvaloniaNativeRenderingMode.Metal }
                },
                _ => new AvaloniaNativePlatformOptions
                {
                    RenderingMode = new[] { AvaloniaNativeRenderingMode.Metal }
                }
            };

            appBuilder = appBuilder.With(options);
        }

        appBuilder.StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
