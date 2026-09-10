using ClipStudio.Application.Services;
using ClipStudio.Core.Enums;
using ClipStudio.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="SteamLibraryScanner"/>, driven against a fake file system shaped like
/// a real Steam install.
/// </summary>
/// <remarks>
/// The scanner resolves its own Steam root from platform-specific locations, so these tests plant
/// their fixture at whichever path <see cref="SteamLibraryScanner"/> looks in first on the platform
/// the run is hosted on. That keeps them honest about the path handling instead of injecting a root
/// the real code would never see, while still passing on all three release runners.
/// </remarks>
public sealed class SteamLibraryScannerTests
{
    /// <summary>
    /// The Steam root the fixture is planted in: the first location the scanner probes on the
    /// platform hosting the test run.
    /// </summary>
    /// <remarks>
    /// Taken from the scanner's own candidate list rather than restated here, because the scanner
    /// finds its own root and never accepts an injected one. A hardcoded Windows path made every
    /// scan return nothing on the Linux and macOS release runners, where no candidate root is under
    /// <c>C:\</c>; reading the list from the scanner means it cannot drift again.
    /// </remarks>
    private static readonly string SteamRoot = SteamLibraryScanner.CandidateRoots.First();

    private static readonly string PrimaryApps = Path.Combine(SteamRoot, "steamapps");

    /// <summary>A second library folder, on a different volume from the Steam install.</summary>
    private static readonly string SecondRoot = Path.Combine(OtherVolume, "SteamLibrary");

    private static readonly string SecondApps = Path.Combine(SecondRoot, "steamapps");

    /// <summary>A volume that is not the one Steam itself is installed on.</summary>
    private static string OtherVolume => OperatingSystem.IsWindows() ? @"G:\" : "/mnt/games";

    private readonly FakeFileSystem _fs = new();

    private SteamLibraryScanner BuildScanner()
        => new(_fs, NullLogger<SteamLibraryScanner>.Instance);

    /// <summary>Writes a libraryfolders.vdf listing the given library roots.</summary>
    private void WithLibraries(params string[] roots)
    {
        var entries = string.Join("\n", roots.Select((r, i) =>
            $"\t\"{i}\"\n\t{{\n\t\t\"path\"\t\t\"{r.Replace("\\", "\\\\")}\"\n\t}}"));

        _fs.AddFile(
            Path.Combine(PrimaryApps, "libraryfolders.vdf"),
            $"\"libraryfolders\"\n{{\n{entries}\n}}\n");
    }

    /// <summary>Writes one app manifest into a steamapps folder.</summary>
    private void WithGame(string steamApps, string appId, string name)
        => _fs.AddFile(
            Path.Combine(steamApps, $"appmanifest_{appId}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"name\"\t\t\"{name}\"\n}}\n");

    [Fact]
    public void ProbesAtLeastOneAbsoluteRootOnThisPlatform()
    {
        // Guards the fixture itself. Every other test here plants its files under the first
        // candidate root, so if this platform offered none - or offered a relative one - they
        // would all fail with an empty result and say nothing about why.
        var roots = SteamLibraryScanner.CandidateRoots.ToList();

        Assert.NotEmpty(roots);
        Assert.All(roots, root => Assert.True(Path.IsPathRooted(root), $"Not an absolute path: {root}"));
    }

    [Fact]
    public void ReportsUnavailableWhenSteamIsNotInstalled()
    {
        Assert.False(BuildScanner().IsAvailable);
    }

    [Fact]
    public async Task ScansNothingWhenSteamIsNotInstalled()
    {
        Assert.Empty(await BuildScanner().ScanAsync());
    }

    [Fact]
    public async Task ReadsGamesFromThePrimaryLibrary()
    {
        WithLibraries(SteamRoot);
        WithGame(PrimaryApps, "10180", "Call of Duty: Modern Warfare 2 (2009)");

        var scanner = BuildScanner();
        Assert.True(scanner.IsAvailable);

        var game = Assert.Single(await scanner.ScanAsync());
        Assert.Equal("Call of Duty: Modern Warfare 2 (2009)", game.Name);
        Assert.Equal("10180", game.StoreAppId);
        Assert.Equal(GameLauncher.Steam, game.Launcher);
        Assert.True(game.IsLikelyGame);
    }

    [Fact]
    public async Task ReadsGamesFromEverySecondaryLibrary()
    {
        // The machine this was built against has two libraries, one of them on another drive.
        WithLibraries(SteamRoot, SecondRoot);
        WithGame(PrimaryApps, "228980", "Steamworks Common Redistributables");
        WithGame(SecondApps, "251570", "7 Days to Die");

        var games = await BuildScanner().ScanAsync();

        Assert.Equal(2, games.Count);
        Assert.Contains(games, g => g.Name == "7 Days to Die");
    }

    [Fact]
    public async Task ATitleInTwoLibrariesIsOneGame()
    {
        WithLibraries(SteamRoot, SecondRoot);
        WithGame(PrimaryApps, "251570", "7 Days to Die");
        WithGame(SecondApps, "251570", "7 Days to Die");

        Assert.Single(await BuildScanner().ScanAsync());
    }

    [Theory]
    [InlineData("7 Days to Die Dedicated Server", "a dedicated server, not a game")]
    [InlineData("Satisfactory Soundtrack", "a soundtrack")]
    [InlineData("Steamworks Common Redistributables", "a redistributable package")]
    [InlineData("ULTRAKILL Demo", "a demo")]
    [InlineData("Battlefield 6 Open Beta", "a beta client")]
    public async Task FlagsThingsThatAreNotGamesWithoutHidingThem(string name, string reason)
    {
        // All five of these are real entries from a 248-title library. They are listed - hiding
        // them would be guessing on the user's behalf - but they arrive unticked and say why.
        WithLibraries(SteamRoot);
        WithGame(PrimaryApps, "1", name);

        var game = Assert.Single(await BuildScanner().ScanAsync());

        Assert.False(game.IsLikelyGame);
        Assert.Equal(reason, game.ExcludedReason);
    }

    [Fact]
    public async Task ARealGameIsNotFlagged()
    {
        WithLibraries(SteamRoot);
        WithGame(PrimaryApps, "526870", "Satisfactory");

        var game = Assert.Single(await BuildScanner().ScanAsync());

        Assert.True(game.IsLikelyGame);
        Assert.Null(game.ExcludedReason);
    }

    [Fact]
    public async Task ResultsAreOrderedByName()
    {
        WithLibraries(SteamRoot);
        WithGame(PrimaryApps, "1", "Warframe");
        WithGame(PrimaryApps, "2", "Abiotic Factor");
        WithGame(PrimaryApps, "3", "Satisfactory");

        var names = (await BuildScanner().ScanAsync()).Select(g => g.Name).ToList();

        Assert.Equal(["Abiotic Factor", "Satisfactory", "Warframe"], names);
    }

    [Fact]
    public async Task AManifestWithNoNameIsSkipped()
    {
        WithLibraries(SteamRoot);
        _fs.AddFile(
            Path.Combine(PrimaryApps, "appmanifest_1.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"1\"\n}\n");
        WithGame(PrimaryApps, "2", "Satisfactory");

        var game = Assert.Single(await BuildScanner().ScanAsync());
        Assert.Equal("Satisfactory", game.Name);
    }

    [Fact]
    public async Task ALibraryFolderThatNoLongerExistsIsSkipped()
    {
        // An unplugged drive should cost its own games, not the scan.
        WithLibraries(SteamRoot, Path.Combine(OtherVolume, "GoneLibrary"));
        WithGame(PrimaryApps, "526870", "Satisfactory");

        var game = Assert.Single(await BuildScanner().ScanAsync());
        Assert.Equal("Satisfactory", game.Name);
    }

    [Fact]
    public async Task AnUnreadableLibraryListStillYieldsThePrimaryLibrary()
    {
        // The file exists but is not KeyValues at all; the folder holding it is still a library.
        _fs.AddFile(Path.Combine(PrimaryApps, "libraryfolders.vdf"), "this is not a vdf file");
        WithGame(PrimaryApps, "526870", "Satisfactory");

        var game = Assert.Single(await BuildScanner().ScanAsync());
        Assert.Equal("Satisfactory", game.Name);
    }
}
