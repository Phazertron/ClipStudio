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
        // Resolve the data directory. An optional --profile <name> argument redirects all
        // persistent storage to %AppData%\ClipStudio_<name>\ so multiple isolated profiles
        // (e.g. a "demo" profile for screenshots) can coexist without touching the real library.
        var profileName = ParseProfileArg(args);
        var folderName  = profileName is not null ? $"ClipStudio_{profileName}" : "ClipStudio";
        App.AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            folderName);

        // Strip --profile and its value from the args passed to Avalonia so the framework
        // does not treat them as unknown arguments.
        args = StripProfileArg(args);

        // Compute the settings path early so Serilog can read the configured log level.
        var settingsPath = Path.Combine(App.AppDataPath, "settings.json");

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

    /// <summary>
    /// Returns the value of the <c>--profile</c> argument, or <see langword="null"/> if it was
    /// not supplied.
    /// </summary>
    private static string? ParseProfileArg(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--profile", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    /// <summary>
    /// Returns a copy of <paramref name="args"/> with the <c>--profile &lt;name&gt;</c> pair
    /// removed so Avalonia does not see unknown arguments.
    /// </summary>
    private static string[] StripProfileArg(string[] args)
    {
        var result = new System.Collections.Generic.List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                i++; // skip the value too
                continue;
            }
            result.Add(args[i]);
        }
        return result.ToArray();
    }
}
