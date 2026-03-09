using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Serilog;
using Velopack;

namespace ClipStudio.UI;

/// <summary>
/// Application entry point. Initialises the Velopack update framework and then the Avalonia host.
/// </summary>
internal sealed class Program
{
    /// <summary>
    /// GitHub repository URL used for automatic update checks.
    /// Set this to <c>https://github.com/OWNER/ClipStudio</c> before publishing a release.
    /// Leave <see langword="null"/> to disable background update checks entirely.
    /// </summary>
    private static readonly string? AutoUpdateRepositoryUrl = null;

    /// <summary>
    /// Main entry point. Velopack's bootstrap call MUST be the very first statement so that
    /// the framework can intercept install/update/uninstall lifecycle commands before the UI
    /// is constructed.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        // Compute the settings path early so Serilog can read the configured log level.
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipStudio", "settings.json");

        App.SetupSerilog(settingsPath);

        // Hook global exception handlers BEFORE the UI starts so no crash escapes silently.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception
                  ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown error");
            Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating}).", args.IsTerminating);
            Log.CloseAndFlush();
            CrashReporter.WriteCrashDump(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception.");
            args.SetObserved();
        };

        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        Log.Information("ClipStudio shutting down.");
        Log.CloseAndFlush();
    }

    /// <summary>
    /// Checks for available updates from the configured GitHub repository source and silently
    /// downloads them in the background. The downloaded update is applied the next time the
    /// application is restarted. Failures are swallowed — update checks are best-effort and
    /// must never affect the user experience.
    /// </summary>
    internal static async Task TryCheckForUpdatesAsync()
    {
        if (AutoUpdateRepositoryUrl is null)
            return;

        try
        {
            var mgr          = new UpdateManager(AutoUpdateRepositoryUrl);
            var newVersion   = await mgr.CheckForUpdatesAsync();
            if (newVersion is not null)
                await mgr.DownloadUpdatesAsync(newVersion);
        }
        catch
        {
            // Update checks are best-effort; network errors or misconfigured URLs must
            // never surface to the user.
        }
    }

    /// <summary>
    /// Avalonia configuration entry point. Also used by the visual designer — do not move or rename.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
