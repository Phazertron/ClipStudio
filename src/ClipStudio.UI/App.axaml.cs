using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using AvaloniaApp = Avalonia.Application;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClipStudio.Application;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Interfaces;
using ClipStudio.Data;
using ClipStudio.UI.Services;
using ClipStudio.UI.ViewModels;
using ClipStudio.UI.ViewModels.WizardSteps;
using ClipStudio.UI.Views;
using FFMpegCore;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace ClipStudio.UI;

/// <summary>
/// Root Avalonia application class. Owns the DI service container and wires it to the
/// Avalonia application lifecycle.
/// </summary>
public partial class App : AvaloniaApp
{
    /// <summary>Gets the application-wide DI service provider. Available after <see cref="Initialize"/>.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Gets or sets the root data directory for all persistent application state (database,
    /// settings, cache, logs). Defaults to <c>%AppData%\ClipStudio</c>; overridden by
    /// <c>Program.Main</c> when a <c>--profile</c> argument is supplied.
    /// </summary>
    public static string AppDataPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipStudio");

    /// <summary>Gets the absolute path to the directory that contains rolling log files.</summary>
    public static string LogsFolder => Path.Combine(AppDataPath, "logs");

    /// <summary>
    /// Initialises the Serilog rolling-file logger.
    /// Must be called before <see cref="BuildServiceProvider"/> so that logging is available
    /// during service construction. Reads the minimum log level from the settings file if it
    /// exists; falls back to <c>Error</c> so the log stays quiet during normal use.
    /// </summary>
    /// <param name="settingsPath">Absolute path to the settings JSON file.</param>
    internal static void SetupSerilog(string settingsPath)
    {
        // Determine the minimum log level from persisted settings (if available).
        LogEventLevel level = LogEventLevel.Error;
        try
        {
            if (File.Exists(settingsPath))
            {
                var json   = File.ReadAllText(settingsPath);
                // Simple string search to avoid a full JSON parse dependency here.
                if (json.Contains("\"MinimumLogLevel\""))
                {
                    foreach (var candidate in new[] {
                        "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" })
                    {
                        if (json.Contains($"\"{candidate}\"") &&
                            Enum.TryParse<LogEventLevel>(candidate, out var parsed))
                        {
                            level = parsed;
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback to Error level if settings cannot be read.
        }

        Directory.CreateDirectory(LogsFolder);
        var logPath = Path.Combine(LogsFolder, "clipstudio-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("ClipStudio starting up.");
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        Services = BuildServiceProvider();
        // Note: migrations and LibVLC initialisation are deferred to after the window is shown,
        // so the app appears immediately rather than blocking for several seconds.
        ConfigureFfmpeg();
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        // Catch any exception that escapes the Avalonia dispatcher loop (e.g. from
        // synchronous view creation or property-change handlers) so the app does not
        // hard-crash.
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            args.Handled = true;
            Log.Error(args.Exception, "Unhandled UI-thread exception.");
            System.Diagnostics.Debug.WriteLine(
                $"[ClipStudio] Unhandled UI exception ({args.Exception.GetType().FullName}): {args.Exception}");
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var settingsService = Services.GetRequiredService<ISettingsService>();

            if (settingsService.Current.IsFirstRun)
            {
                // First-run: show the setup wizard immediately, run heavy init in the background.
                // The wizard doesn't need the database until the user clicks Finish.
                var wizardScope = Services.CreateScope();
                var wizardVm    = wizardScope.ServiceProvider.GetRequiredService<SetupWizardViewModel>();
                var wizard      = new SetupWizardWindow { DataContext = wizardVm };
                desktop.MainWindow = wizard;

                // Run migrations and LibVLC init in parallel while the wizard is open.
                _ = Task.Run(async () =>
                {
                    await Services.ApplyMigrationsAsync();
                    _ = Services.GetRequiredService<LibVLC>();
                });

                wizardVm.Completed += () =>
                {
                    wizardScope.Dispose();

                    // Show the main window with the same loading overlay used by the non-first-run
                    // path so VLC is pre-warmed and the library is populated before the user can
                    // interact.  Migrations and LibVLC init ran in the background during the wizard,
                    // so those steps are fast / no-ops here.
                    var mainWindowVm = Services.GetRequiredService<MainWindowViewModel>();
                    mainWindowVm.IsStartupLoading  = true;
                    mainWindowVm.StartupStatusText = "Starting up...";

                    var mainWindow = new MainWindow { DataContext = mainWindowVm };
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    wizard.Close();

                    _ = InitializeServicesAsync(mainWindowVm);
                };
            }
            else
            {
                // Non-first-run: show main window immediately with a loading overlay.
                var mainWindowVm = Services.GetRequiredService<MainWindowViewModel>();
                mainWindowVm.IsStartupLoading  = true;
                mainWindowVm.StartupStatusText = "Starting up...";

                desktop.MainWindow = new MainWindow { DataContext = mainWindowVm };

                // Run heavy initialisation in the background; dismiss overlay when done.
                _ = InitializeServicesAsync(mainWindowVm);
            }
        }

        // Check for updates silently in the background; result is applied on next restart.
        _ = Program.TryCheckForUpdatesAsync();

        // Show crash report dialog if a dump from the previous session was found.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime dl2)
            _ = ShowPendingCrashReportAsync(dl2);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Runs database migrations and LibVLC initialisation on a background thread,
    /// updating the startup-status text at each step, then starts library watchers
    /// and dismisses the loading overlay.
    /// Each step is independently fault-tolerant: a failure in one step (e.g. VLC warmup)
    /// does not prevent subsequent steps (e.g. library preload) from running.
    /// </summary>
    private static async Task InitializeServicesAsync(MainWindowViewModel vm)
    {
        // Step 1: Apply pending database migrations.
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => vm.StartupStatusText = "Applying database migrations...");
            await Services.ApplyMigrationsAsync();
        }
        catch
        {
            // Non-fatal: continue; the app can operate on the existing schema.
        }

        // Step 2: Pre-warm the VLC MediaPlayer subsystem.
        // Creating the first MediaPlayer loads VLC's audio/video output plugins, which otherwise
        // causes a perceptible freeze when the user opens their first clip.
        // This is best-effort: on some platforms (e.g. thread-apartment restrictions on Windows)
        // MediaPlayer creation on a thread-pool thread may fail; we swallow the exception so the
        // rest of initialisation continues and VLC initialises lazily on first use instead.
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => vm.StartupStatusText = "Initialising video engine...");
            await Task.Run(() =>
            {
                var libVlc = Services.GetRequiredService<LibVLC>();
                try
                {
                    using var warmup = new LibVLCSharp.Shared.MediaPlayer(libVlc);
                }
                catch
                {
                    // Warmup failed; video engine will initialise on first clip open instead.
                }
            });
        }
        catch
        {
            // Non-fatal: continue with library load.
        }

        // Step 3: Start file-system watchers and pre-load the clip library.
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => vm.StartupStatusText = "Loading library...");
            StartLibraryWatchers();

            // Pre-load the library to populate the clip grid and warm up EF Core before the
            // startup overlay is dismissed, so the user can interact without any stall.
            await Dispatcher.UIThread.InvokeAsync(() => vm.PreloadLibraryAsync());
        }
        catch
        {
            // Non-fatal: library will load on demand when the user interacts with the grid.
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.IsStartupLoading  = false;
            vm.StartupStatusText = string.Empty;
        });
    }

    /// <summary>
    /// If any crash dump files from the previous session exist, shows the first one
    /// in a modal <see cref="CrashReportDialog"/> so the user can report or dismiss it.
    /// </summary>
    private static async Task ShowPendingCrashReportAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Give the main window a moment to appear before showing the dialog.
        await Task.Delay(1500);

        var dumps = CrashReporter.GetPendingCrashDumps();
        if (dumps.Length == 0)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var dumpPath = dumps[0];
            var vm = new CrashReportDialogViewModel
            {
                CrashText    = CrashReporter.ReadCrashDump(dumpPath),
                DumpFilePath = dumpPath,
            };
            var dialog = new CrashReportDialog(vm);
            if (desktop.MainWindow is not null)
                dialog.ShowDialog(desktop.MainWindow);
        });
    }

    private static IServiceProvider BuildServiceProvider()
    {
        Directory.CreateDirectory(AppDataPath);

        var dbPath       = Path.Combine(AppDataPath, "library.db");
        var settingsPath = Path.Combine(AppDataPath, "settings.json");

        var services = new ServiceCollection();

        services.AddLogging(logging => logging.AddSerilog(dispose: false));

        services.AddClipStudioData(dbPath);
        services.AddClipStudioApplication(settingsPath, AppDataPath);

        // LibVLC — single shared instance for the lifetime of the app.
        // On macOS the dylibs and plugins live inside the VLC.app bundle and are not
        // on the dynamic linker path. Both must be pointed at explicitly before the
        // first LibVLC instance is created.
        if (OperatingSystem.IsMacOS())
        {
            var vlcBase    = "/Applications/VLC.app/Contents/MacOS";
            var vlcLib     = Path.Combine(vlcBase, "lib");
            var vlcPlugins = Path.Combine(vlcBase, "plugins");
            if (Directory.Exists(vlcLib))
            {
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", vlcPlugins);
                LibVLCSharp.Shared.Core.Initialize(vlcLib);
            }
        }

        services.AddSingleton<LibVLC>(_ => new LibVLC(enableDebugLogs: false));

        // Sound effects — singleton so the SoundPlayer instance is reused across calls
        services.AddSingleton<ISoundService, SoundService>();

        // ViewModels — setup wizard
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<WelcomeStepViewModel>();
        services.AddTransient<SourceFoldersStepViewModel>();
        services.AddTransient<FfmpegStepViewModel>();
        services.AddTransient<TranscriptionSetupStepViewModel>();
        services.AddTransient<FinishStepViewModel>();

        // ViewModels — main app
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<UnreviewedQueueViewModel>();
        services.AddTransient<TagManagerViewModel>();
        services.AddTransient<GamesViewModel>();
        services.AddTransient<PlayersViewModel>();
        // SettingsViewModel is a singleton so that an in-progress import scan continues
        // running when the user navigates away from the Settings tab and then returns.
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ExportQueueViewModel>();
        services.AddTransient<HighlightsPageViewModel>();
        services.AddTransient<TrashPageViewModel>();
        services.AddTransient<StatsPageViewModel>();
        services.AddScoped<ClipDetailViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Configures FFMpegCore's binary folder from user settings or auto-detects a bundled
    /// <c>ffmpeg/</c> subfolder next to the application executable.
    /// Falls back to the system PATH if neither is available.
    /// </summary>
    private static void ConfigureFfmpeg()
    {
        var settings = Services.GetRequiredService<ISettingsService>().Current;

        var folder = settings.FfmpegBinaryFolder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            // Auto-detect a bundled ffmpeg folder next to the executable.
            var candidate = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            if (Directory.Exists(candidate))
                folder = candidate;
        }

        if (!string.IsNullOrWhiteSpace(folder))
            GlobalFFOptions.Configure(options => options.BinaryFolder = folder);
    }

    private static void StartLibraryWatchers()
    {
        // Re-start file system watchers for all active source folders persisted in the database.
        // This ensures new clips are detected automatically on app restart, not just after the wizard.
        _ = Task.Run(async () =>
        {
            try
            {
                var watcher         = Services.GetRequiredService<ILibraryWatcherService>();
                var settingsService = Services.GetRequiredService<ISettingsService>();

                // ISourceFolderRepository is scoped; create a short-lived scope for this query.
                using var scope = Services.CreateScope();
                var folderRepo  = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();
                var folders     = await folderRepo.GetActiveAsync();

                foreach (var folder in folders)
                    watcher.StartWatching(folder.Path, folder.Id);

                // Optional startup scan: import any files dropped into watched folders while the app was closed.
                // Routed through SettingsViewModel so progress is shown on the Settings page
                // and LastScannedDisplay is updated on each source folder row after completion.
                if (settingsService.Current.AutoScanAtStartup)
                {
                    var settingsVm = Services.GetRequiredService<SettingsViewModel>();
                    // Load the source folder rows into SettingsViewModel (must be on the UI thread).
                    await Dispatcher.UIThread.InvokeAsync(async () => await settingsVm.LoadAsync());
                    // Run the scan via ScanAllCommand, which reports per-folder progress and
                    // refreshes LastScannedDisplay on each row after each folder finishes.
                    await Dispatcher.UIThread.InvokeAsync(async () => await settingsVm.ScanAllCommand.ExecuteAsync(null));
                }

                // Purge trash items older than 30 days on every startup.
                var clipService = scope.ServiceProvider.GetRequiredService<IClipService>();
                await clipService.PurgeExpiredTrashAsync();

                // Pre-download game cover art so images are ready from disk when user visits Games page.
                var gamesVm = Services.GetRequiredService<GamesViewModel>();
                await gamesVm.PreloadCoversAsync();

                // Regenerate any missing thumbnails/strips and remove orphaned cache files.
                var sanitizer = scope.ServiceProvider.GetRequiredService<ILibrarySanitizerService>();
                await sanitizer.SanitizeAsync();
            }
            catch
            {
                // Non-fatal: watchers will simply not fire until the user visits Settings.
            }
        });
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
