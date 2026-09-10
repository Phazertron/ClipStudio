using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Parsing;
using ClipStudio.Core.Enums;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Reads the games Steam has installed, across every library folder it knows about.
/// </summary>
/// <remarks>
/// Steam records its library folders in <c>steamapps/libraryfolders.vdf</c> and one
/// <c>appmanifest_&lt;appid&gt;.acf</c> per installed title inside each folder's <c>steamapps</c>.
/// Both are read; nothing is written.
/// </remarks>
public sealed class SteamLibraryScanner : IInstalledGameScanner
{
    /// <summary>
    /// Name fragments that mark an entry as something other than a game.
    /// </summary>
    /// <remarks>
    /// Steam installs dedicated servers, soundtracks, demos and redistributables as separate apps
    /// with their own manifests. On a real 248-game library thirteen entries matched these, which
    /// is thirteen tags nobody wants to untick by hand. They are still listed - hiding them would
    /// be guessing on the user's behalf - but they arrive unticked and say why.
    /// </remarks>
    private static readonly (string Fragment, string Reason)[] NonGameMarkers =
    [
        ("Dedicated Server", "a dedicated server, not a game"),
        ("Soundtrack",       "a soundtrack"),
        ("Redistributables", "a redistributable package"),
        ("SDK",              "a development kit"),
        (" Demo",            "a demo"),
        ("Open Beta",        "a beta client"),
        ("Benchmark",        "a benchmark"),
    ];

    private readonly IFileSystem _fileSystem;
    private readonly ILogger<SteamLibraryScanner> _logger;

    /// <inheritdoc/>
    public GameLauncher Launcher => GameLauncher.Steam;

    /// <inheritdoc/>
    public bool IsAvailable => FindLibraryFoldersFile() is not null;

    /// <summary>Initialises a new <see cref="SteamLibraryScanner"/>.</summary>
    /// <param name="fileSystem">The file system abstraction, so the scan is testable without Steam.</param>
    /// <param name="logger">The logger.</param>
    public SteamLibraryScanner(IFileSystem fileSystem, ILogger<SteamLibraryScanner> logger)
    {
        _fileSystem = fileSystem;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<InstalledGame>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var manifestFile = FindLibraryFoldersFile();
        if (manifestFile is null)
        {
            _logger.LogDebug("Steam does not appear to be installed.");
            return Task.FromResult<IReadOnlyList<InstalledGame>>([]);
        }

        var games = new List<InstalledGame>();

        // Keyed by AppId: a title present in two library folders is one game, not two.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var steamApps in ReadLibraryFolders(manifestFile))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_fileSystem.DirectoryExists(steamApps)) continue;

            foreach (var manifest in _fileSystem.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var game = ReadManifest(manifest);
                if (game is null) continue;

                if (game.StoreAppId is { Length: > 0 } appId && !seen.Add(appId)) continue;

                games.Add(game);
            }
        }

        _logger.LogInformation(
            "Steam scan found {Count} installed title(s), {Games} of them likely games.",
            games.Count, games.Count(g => g.IsLikelyGame));

        return Task.FromResult<IReadOnlyList<InstalledGame>>(
            games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    /// <summary>
    /// Returns every <c>steamapps</c> directory Steam knows about, starting with the install root.
    /// </summary>
    /// <param name="libraryFoldersFile">The path to <c>libraryfolders.vdf</c>.</param>
    /// <returns>The directories to look for manifests in.</returns>
    private IEnumerable<string> ReadLibraryFolders(string libraryFoldersFile)
    {
        // The file lives inside the primary steamapps folder, which is itself a library.
        var primary = Path.GetDirectoryName(libraryFoldersFile);
        if (primary is not null) yield return primary;

        ValveKeyValueNode parsed;
        try
        {
            parsed = ValveKeyValueParser.Parse(_fileSystem.ReadAllText(libraryFoldersFile));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Steam's library folder list.");
            yield break;
        }

        var root = parsed.Child("libraryfolders");
        if (root is null) yield break;

        // Entries are numbered blocks, each with a "path". Older clients stored the path as a
        // scalar under the same numeric key instead, so both shapes are read.
        foreach (var (key, child) in root.Children)
        {
            var path = child.Value("path");
            if (!string.IsNullOrWhiteSpace(path))
                yield return Path.Combine(path, "steamapps");
            else
                _logger.LogDebug("Steam library entry {Key} had no path.", key);
        }

        foreach (var (_, value) in root.Values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                yield return Path.Combine(value, "steamapps");
        }
    }

    /// <summary>Reads one app manifest.</summary>
    /// <param name="manifestPath">The manifest to read.</param>
    /// <returns>The game, or null when the manifest was unusable.</returns>
    private InstalledGame? ReadManifest(string manifestPath)
    {
        try
        {
            var state = ValveKeyValueParser.Parse(_fileSystem.ReadAllText(manifestPath))
                .Child("AppState");

            var name = state?.Value("name");
            if (string.IsNullOrWhiteSpace(name)) return null;

            var (isGame, reason) = Classify(name);

            return new InstalledGame(
                name.Trim(),
                GameLauncher.Steam,
                state?.Value("appid"),
                isGame,
                reason);
        }
        catch (Exception ex)
        {
            // One unreadable manifest should cost one game, not the whole scan.
            _logger.LogDebug(ex, "Could not read Steam manifest '{Path}'.", manifestPath);
            return null;
        }
    }

    /// <summary>Decides whether an installed title looks like a game.</summary>
    /// <param name="name">The title.</param>
    /// <returns>Whether it is likely a game, and if not, why not.</returns>
    private static (bool IsGame, string? Reason) Classify(string name)
    {
        foreach (var (fragment, reason) in NonGameMarkers)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return (false, reason);
        }

        return (true, null);
    }

    /// <summary>
    /// Locates <c>libraryfolders.vdf</c> in the usual place for this platform.
    /// </summary>
    /// <returns>The path, or null when Steam is not installed.</returns>
    private string? FindLibraryFoldersFile()
        => CandidateSteamRoots()
            .Select(root => Path.Combine(root, "steamapps", "libraryfolders.vdf"))
            .FirstOrDefault(_fileSystem.FileExists);

    /// <summary>Returns the places Steam is normally installed, per platform.</summary>
    /// <returns>Candidate Steam root directories.</returns>
    private static IEnumerable<string> CandidateSteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\Program Files (x86)\Steam";
            yield return @"C:\Program Files\Steam";

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (localAppData.Length > 0)
                yield return Path.Combine(localAppData, "Steam");

            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length == 0) yield break;

        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
            yield break;
        }

        // Linux, including the Flatpak location.
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
    }
}
