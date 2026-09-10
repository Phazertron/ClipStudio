using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.UI.ViewModels;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="InstalledGameImportViewModel"/>.
/// </summary>
/// <remarks>
/// A real Steam library holds hundreds of titles, so what matters here is that nothing is created
/// without being ticked and that the default ticks are sensible - not that everything gets
/// imported.
/// </remarks>
public sealed class InstalledGameImportViewModelTests
{
    private readonly Mock<IInstalledGameScanner> _scanner = new();
    private readonly Mock<ITagService> _tags = new();

    public InstalledGameImportViewModelTests()
    {
        _scanner.Setup(x => x.Launcher).Returns(GameLauncher.Steam);
        _scanner.Setup(x => x.IsAvailable).Returns(true);
        _scanner.Setup(x => x.ScanAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _tags.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _tags.Setup(x => x.CreateFromSteamAsync(It.IsAny<SteamGame>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag());
        _tags.Setup(x => x.CreateCustomGameTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag());
    }

    private InstalledGameImportViewModel Build() => new([_scanner.Object], _tags.Object);

    private void Found(params InstalledGame[] games) =>
        _scanner.Setup(x => x.ScanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(games);

    private void ExistingGames(params string[] names) =>
        _tags.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(names.Select(n => new Tag { Name = n, Type = TagType.Game }).ToList());

    private static InstalledGame Game(string name, string? appId = "1", bool likely = true, string? reason = null)
        => new(name, GameLauncher.Steam, appId, likely, reason);

    // ---- Availability ----

    [Fact]
    public void ReportsWhenNoLauncherIsInstalled()
    {
        _scanner.Setup(x => x.IsAvailable).Returns(false);
        var vm = Build();

        Assert.False(vm.AnyLauncherAvailable);
        Assert.Equal("no launchers found", vm.AvailableLaunchersDisplay);
    }

    [Fact]
    public void NamesTheLaunchersItFound()
    {
        Assert.True(Build().AnyLauncherAvailable);
        Assert.Equal("Steam", Build().AvailableLaunchersDisplay);
    }

    // ---- Default ticks ----

    [Fact]
    public async Task ARealGameStartsTicked()
    {
        Found(Game("Satisfactory"));
        var vm = Build();

        await vm.ScanCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Games);
        Assert.True(row.IsSelected);
        Assert.Null(row.Reason);
        Assert.Equal(1, vm.SelectedCount);
    }

    [Fact]
    public async Task SomethingThatIsNotAGameIsListedButUnticked()
    {
        // Hiding it would be guessing on the user's behalf; ticking it would flood the tag list.
        Found(Game("Satisfactory Soundtrack", likely: false, reason: "a soundtrack"));
        var vm = Build();

        await vm.ScanCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Games);
        Assert.False(row.IsSelected);
        Assert.Equal("a soundtrack", row.Reason);
    }

    [Fact]
    public async Task AGameAlreadyInTheLibraryIsUntickedAndSaysSo()
    {
        // Matched by name and left alone rather than duplicated, so importing twice is safe.
        ExistingGames("Satisfactory");
        Found(Game("Satisfactory"));
        var vm = Build();

        await vm.ScanCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Games);
        Assert.False(row.IsSelected);
        Assert.True(row.AlreadyInLibrary);
        Assert.Equal("already in your library", row.Reason);
    }

    [Fact]
    public async Task MatchingAnExistingGameIgnoresCaseAndSurroundingSpace()
    {
        ExistingGames("  satisfactory  ");
        Found(Game("Satisfactory"));
        var vm = Build();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(vm.Games).AlreadyInLibrary);
    }

    // ---- Selection ----

    [Fact]
    public async Task TickAllSkipsWhatIsAlreadyInTheLibrary()
    {
        // Ticking those would create duplicates.
        ExistingGames("Satisfactory");
        Found(Game("Satisfactory"), Game("Warframe"), Game("A Soundtrack", likely: false, reason: "a soundtrack"));
        var vm = Build();
        await vm.ScanCommand.ExecuteAsync(null);

        vm.SelectAllCommand.Execute(null);

        Assert.False(vm.Games.Single(g => g.Name == "Satisfactory").IsSelected);
        Assert.True(vm.Games.Single(g => g.Name == "Warframe").IsSelected);
        Assert.True(vm.Games.Single(g => g.Name == "A Soundtrack").IsSelected);
    }

    [Fact]
    public async Task UntickAllClearsEverything()
    {
        Found(Game("Satisfactory"), Game("Warframe"));
        var vm = Build();
        await vm.ScanCommand.ExecuteAsync(null);

        vm.SelectNoneCommand.Execute(null);

        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.CanImport);
    }

    [Fact]
    public async Task TheCountAndLabelFollowTheTicks()
    {
        Found(Game("Satisfactory"), Game("Warframe"));
        var vm = Build();
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Import 2 games", vm.ImportLabel);

        vm.Games[0].IsSelected = false;

        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal("Import 1 game", vm.ImportLabel);
    }

    // ---- Importing ----

    [Fact]
    public async Task ImportsOnlyWhatIsTicked()
    {
        Found(Game("Satisfactory", "526870"), Game("Warframe", "230410"));
        var vm = Build();
        await vm.ScanCommand.ExecuteAsync(null);
        vm.Games.Single(g => g.Name == "Warframe").IsSelected = false;

        await vm.ImportCommand.ExecuteAsync(null);

        _tags.Verify(x => x.CreateFromSteamAsync(
            It.Is<SteamGame>(g => g.Name == "Satisfactory"), It.IsAny<CancellationToken>()), Times.Once);
        _tags.Verify(x => x.CreateFromSteamAsync(
            It.Is<SteamGame>(g => g.Name == "Warframe"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASteamTitleIsImportedWithItsAppIdAndCoverArt()
    {
        // The AppId comes free from the manifest, so an imported game gets its art without a search.
        Found(Game("Satisfactory", "526870"));
        var vm = Build();
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        _tags.Verify(x => x.CreateFromSteamAsync(
            It.Is<SteamGame>(g => g.AppId == 526870 && g.CoverUrl!.Contains("526870")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ATitleWithoutAnIdentifierFallsBackToAPlainGameTag()
    {
        Found(Game("Some Game", appId: null));
        var vm = Build();
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        _tags.Verify(x => x.CreateCustomGameTagAsync("Some Game", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OneBadTitleDoesNotCostTheRestOfTheImport()
    {
        Found(Game("Bad", "1"), Game("Good", "2"));
        _tags.Setup(x => x.CreateFromSteamAsync(
                 It.Is<SteamGame>(g => g.Name == "Bad"), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("nope"));

        var vm = Build();
        await vm.ScanCommand.ExecuteAsync(null);
        await vm.ImportCommand.ExecuteAsync(null);

        _tags.Verify(x => x.CreateFromSteamAsync(
            It.Is<SteamGame>(g => g.Name == "Good"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("1 failed", vm.StatusMessage);
    }

    [Fact]
    public async Task ImportingTellsTheGamesPageToReload()
    {
        Found(Game("Satisfactory"));
        var vm = Build();
        var reloaded = false;
        vm.Imported = () => { reloaded = true; return Task.CompletedTask; };

        await vm.ScanCommand.ExecuteAsync(null);
        await vm.ImportCommand.ExecuteAsync(null);

        Assert.True(reloaded);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task CancellingCreatesNothing()
    {
        Found(Game("Satisfactory"));
        var vm = Build();
        await vm.ScanCommand.ExecuteAsync(null);

        vm.CancelCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Games);
        _tags.Verify(x => x.CreateFromSteamAsync(It.IsAny<SteamGame>(), It.IsAny<CancellationToken>()), Times.Never);
        _tags.Verify(x => x.CreateCustomGameTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnEmptyScanSaysSoRatherThanOpeningABlankList()
    {
        var vm = Build();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("No installed games were found.", vm.StatusMessage);
        Assert.Empty(vm.Games);
        Assert.False(vm.CanImport);
    }

    [Fact]
    public async Task AFailedScanReportsRatherThanThrowing()
    {
        _scanner.Setup(x => x.ScanAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("drive gone"));
        var vm = Build();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.NotNull(vm.StatusMessage);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task AnUnavailableLauncherIsNotScanned()
    {
        _scanner.Setup(x => x.IsAvailable).Returns(false);
        var vm = Build();

        await vm.ScanCommand.ExecuteAsync(null);

        _scanner.Verify(x => x.ScanAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
