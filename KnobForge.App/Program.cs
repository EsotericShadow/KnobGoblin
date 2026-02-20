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

        string mode = (Environment.GetEnvironmentVariable("KNOBFORGE_RENDER_MODE") ?? "default")
            .Trim()
            .ToLowerInvariant();
        Console.WriteLine($">>> RenderMode={mode}");

        var appBuilder = BuildAvaloniaApp();
        if (OperatingSystem.IsMacOS())
        {
            AvaloniaNativePlatformOptions? options = mode switch
            {
                "opengl" => new AvaloniaNativePlatformOptions
                {
                    RenderingMode = new[] { AvaloniaNativeRenderingMode.OpenGl }
                },
                "metal" => new AvaloniaNativePlatformOptions
                {
                    RenderingMode = new[] { AvaloniaNativeRenderingMode.Metal }
                },
                "software" => new AvaloniaNativePlatformOptions
                {
                    RenderingMode = new[] { AvaloniaNativeRenderingMode.Software }
                },
                _ => null
            };

            if (options != null)
            {
                appBuilder = appBuilder.With(options);
            }
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
