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
/// The scanner resolves its own Steam root from platform-specific locations, so these tests use the
/// Windows default path that <see cref="SteamLibraryScanner"/> looks in first. That keeps them
/// honest about the path handling instead of injecting a root the real code would never see.
/// </remarks>
public sealed class SteamLibraryScannerTests
{
    private const string SteamRoot = @"C:\Program Files (x86)\Steam";
    private const string PrimaryApps = SteamRoot + @"\steamapps";
    private const string SecondApps = @"G:\SteamLibrary\steamapps";

    private readonly FakeFileSystem _fs = new();

    private SteamLibraryScanner BuildScanner()
        => new(_fs, NullLogger<SteamLibraryScanner>.Instance);

    /// <summary>Writes a libraryfolders.vdf listing the given library roots.</summary>
    private void WithLibraries(params string[] roots)
    {
        var entries = string.Join("\n", roots.Select((r, i) =>
            $"\t\"{i}\"\n\t{{\n\t\t\"path\"\t\t\"{r.Replace("\\", "\\\\")}\"\n\t}}"));

        _fs.AddFile($@"{PrimaryApps}\libraryfolders.vdf", $"\"libraryfolders\"\n{{\n{entries}\n}}\n");
    }

    /// <summary>Writes one app manifest into a steamapps folder.</summary>
    private void WithGame(string steamApps, string appId, string name)
        => _fs.AddFile(
            $@"{steamApps}\appmanifest_{appId}.acf",
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"name\"\t\t\"{name}\"\n}}\n");

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
        WithLibraries(SteamRoot, @"G:\SteamLibrary");
        WithGame(PrimaryApps, "228980", "Steamworks Common Redistributables");
        WithGame(SecondApps, "251570", "7 Days to Die");

        var games = await BuildScanner().ScanAsync();

        Assert.Equal(2, games.Count);
        Assert.Contains(games, g => g.Name == "7 Days to Die");
    }

    [Fact]
    public async Task ATitleInTwoLibrariesIsOneGame()
    {
        WithLibraries(SteamRoot, @"G:\SteamLibrary");
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
        _fs.AddFile($@"{PrimaryApps}\appmanifest_1.acf", "\"AppState\"\n{\n\t\"appid\"\t\t\"1\"\n}\n");
        WithGame(PrimaryApps, "2", "Satisfactory");

        var game = Assert.Single(await BuildScanner().ScanAsync());
        Assert.Equal("Satisfactory", game.Name);
    }

    [Fact]
    public async Task ALibraryFolderThatNoLongerExistsIsSkipped()
    {
        // An unplugged drive should cost its own games, not the scan.
        WithLibraries(SteamRoot, @"E:\GoneLibrary");
        WithGame(PrimaryApps, "526870", "Satisfactory");

        var game = Assert.Single(await BuildScanner().ScanAsync());
        Assert.Equal("Satisfactory", game.Name);
    }

    [Fact]
    public async Task AnUnreadableLibraryListStillYieldsThePrimaryLibrary()
    {
        // The file exists but is not KeyValues at all; the folder holding it is still a library.
        _fs.AddFile($@"{PrimaryApps}\libraryfolders.vdf", "this is not a vdf file");
        WithGame(PrimaryApps, "526870", "Satisfactory");

        var game = Assert.Single(await BuildScanner().ScanAsync());
        Assert.Equal("Satisfactory", game.Name);
    }
}
