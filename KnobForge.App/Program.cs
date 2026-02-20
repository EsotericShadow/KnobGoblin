using Avalonia;
using Avalonia.Native;
using System;
using System.IO;
using System.Threading.Tasks;

namespace KnobForge.App;

class Program
{
    private static readonly string FatalLogPath = Path.Combine(Path.GetTempPath(), "knobforge_fatal.log");

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        WireFatalExceptionLogging();

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

        try
        {
            appBuilder.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            string message = $">>> [Fatal] Startup crash: {ex}";
            Console.Error.WriteLine(message);
            AppendFatalLog(message);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WireFatalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string details = e.ExceptionObject is Exception ex
                ? ex.ToString()
                : e.ExceptionObject?.ToString() ?? "<null>";
            AppendFatalLog($">>> [UnhandledException] IsTerminating={e.IsTerminating} {details}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppendFatalLog($">>> [UnobservedTaskException] {e.Exception}");
            e.SetObserved();
        };
    }

    private static void AppendFatalLog(string line)
    {
        try
        {
            File.AppendAllText(
                FatalLogPath,
                $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
        }
        catch
        {
            // best effort only
        }
    }
}
