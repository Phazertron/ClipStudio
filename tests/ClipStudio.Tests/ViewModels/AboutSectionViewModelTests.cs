using ClipStudio.Tests.Fakes;
using ClipStudio.UI.Services;
using ClipStudio.UI.ViewModels.Settings;
using Material.Icons;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="AboutSectionViewModel"/>, which states where this build stands
/// against the latest release and lets the user ask for a check.
/// </summary>
public sealed class AboutSectionViewModelTests
{
    private readonly FakeSettingsSectionHost _host = new();
    private readonly FakeApplicationUpdateService _updates = new();

    private AboutSectionViewModel Build() => new(_host, _updates);

    [Fact]
    public void SaysUpToDateWhenNoNewerReleaseExists()
    {
        _updates.SetStatus(new UpdateStatus { IsSupported = true, CurrentVersion = "1.1.4" });

        var vm = Build();

        Assert.Equal("Up to date.", vm.UpdateStatusText);
        Assert.Equal(MaterialIconKind.CheckCircleOutline, vm.UpdateStatusIcon);
    }

    [Fact]
    public void DoesNotClaimToBeUpToDateWhenItCannotKnow()
    {
        // A publish folder or a debugger session cannot check, so saying "up to date" would be a
        // claim it is in no position to make.
        var vm = Build();

        Assert.Equal("This build does not update itself.", vm.UpdateStatusText);
    }

    [Fact]
    public void NamesTheAvailableVersion()
    {
        _updates.ReportAvailable("1.2.0", currentVersion: "1.1.4");

        var vm = Build();

        Assert.Contains("1.2.0", vm.UpdateStatusText);
        Assert.Contains("available", vm.UpdateStatusText);
    }

    [Fact]
    public void SaysSoWhenTheCheckFailed()
    {
        _updates.SetStatus(new UpdateStatus { IsSupported = true, FailureReason = "no network" });

        var vm = Build();

        Assert.Equal(MaterialIconKind.AlertCircleOutline, vm.UpdateStatusIcon);
    }

    [Fact]
    public async Task CheckingOnDemandAsksTheUpdaterAndDescribesTheResult()
    {
        var vm = Build();
        _updates.SetStatus(new UpdateStatus { IsSupported = true, CurrentVersion = "1.1.4" });

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("Up to date.", vm.UpdateStatusText);
        Assert.False(vm.IsCheckingForUpdates);
    }

    [Fact]
    public void FollowsTheUpdaterWithoutBeingAsked()
    {
        var vm = Build();

        // The periodic check can land at any moment; the section must follow it rather than
        // showing whatever was true when Settings was opened.
        _updates.ReportAvailable("1.3.0");

        Assert.Contains("1.3.0", vm.UpdateStatusText);
    }

    [Fact]
    public void ReportsTheRunningVersionRatherThanAPlaceholder()
    {
        // Guards the 0.1.0 regression: the version must come from the assembly's informational
        // version, which the release workflow stamps.
        var vm = Build();

        Assert.StartsWith("ClipStudio v", vm.AppVersion);
        Assert.DoesNotContain("+", vm.AppVersion);
    }
}
