using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.ViewModels;
using Moq;
using Xunit;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="TrimEditorViewModel"/> covering opening the form, the three ways of
/// moving a trim point, the destructive-trim guard, and queueing the export.
/// </summary>
public sealed class TrimEditorViewModelTests
{
    private const string ClipPath = "/clips/Replay.mp4";

    private readonly FakeTrimEditorHost _host = new();
    private readonly Mock<IExportService> _exportService = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new();

    /// <summary>Sets up a host with a two-minute clip open and non-destructive trimming by default.</summary>
    public TrimEditorViewModelTests()
    {
        _appSettings.DefaultTrimMode = TrimMode.NonDestructive;
        _settings.Setup(s => s.Current).Returns(_appSettings);

        _host.CurrentClip = new Clip
        {
            Id = 1,
            FilePath = ClipPath,
            FileName = "Replay.mp4",
            Duration = TimeSpan.FromMinutes(2)
        };
        _host.DurationSeconds = 120;
    }

    /// <summary>Builds the editor under test.</summary>
    /// <returns>An editor wired to the fakes.</returns>
    private TrimEditorViewModel Create() => new(_host, _exportService.Object, _settings.Object);

    /// <summary>Builds an editor with its form already open.</summary>
    /// <returns>An open editor.</returns>
    private TrimEditorViewModel CreateOpen()
    {
        var vm = Create();
        vm.BeginTrimCommand.Execute(null);
        return vm;
    }

    /// <summary>Builds a highlight view model carrying only the range and label the trim check reads.</summary>
    /// <param name="id">The highlight identifier.</param>
    /// <param name="startSeconds">The range start.</param>
    /// <param name="endSeconds">The range end.</param>
    /// <param name="label">The label shown in the warning.</param>
    /// <returns>A highlight view model.</returns>
    private static HighlightViewModel Highlight(int id, double startSeconds, double endSeconds, string label)
        => new(
            new Highlight
            {
                Id = id,
                Label = label,
                StartTime = TimeSpan.FromSeconds(startSeconds),
                EndTime = TimeSpan.FromSeconds(endSeconds)
            },
            TimeSpan.FromMinutes(2),
            _ => { },
            _ => Task.CompletedTask,
            [],
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

    // ---- Opening the form ----

    [Fact]
    public void BeginTrim_InWatchMode_DoesNothing()
    {
        _host.CanBeginTrim = false;
        var vm = Create();

        vm.BeginTrimCommand.Execute(null);

        Assert.False(vm.IsTrimming);
        Assert.Equal(0, _host.PrepareForTrimCalls);
    }

    [Fact]
    public void BeginTrim_ClosesTheHighlightFormFirst()
    {
        var vm = CreateOpen();

        Assert.True(vm.IsTrimming);
        Assert.Equal(1, _host.PrepareForTrimCalls);
    }

    [Fact]
    public void BeginTrim_SeedsTheRangeToTheWholeClip()
    {
        var vm = CreateOpen();

        Assert.Equal("0:00", vm.TrimStartDisplay);
        Assert.Equal("2:00", vm.TrimEndDisplay);
        Assert.Equal(0, vm.TrimStartFraction, 6);
        Assert.Equal(1, vm.TrimEndFraction, 6);
    }

    [Fact]
    public void BeginTrim_SuggestsATrimmedFileBesideTheOriginal()
    {
        var vm = CreateOpen();

        Assert.EndsWith("Replay_trimmed.mp4", vm.TrimOutputPath);
    }

    [Fact]
    public void BeginTrim_KeepsAnOutputPathTheUserAlreadyChose()
    {
        var vm = Create();
        vm.TrimOutputPath = "/elsewhere/mine.mp4";

        vm.BeginTrimCommand.Execute(null);

        Assert.Equal("/elsewhere/mine.mp4", vm.TrimOutputPath);
    }

    [Fact]
    public void BeginTrim_TakesTheDestructiveDefaultFromSettings()
    {
        _appSettings.DefaultTrimMode = TrimMode.Destructive;

        Assert.True(CreateOpen().IsTrimDestructive);
    }

    [Fact]
    public void ClosingTheForm_ClearsTheDestructiveWarningAndRepointsPrecision()
    {
        var vm = CreateOpen();
        var callsAfterOpen = _host.TrimmingChangedCalls;
        vm.TrimDestructiveWarning = "something";

        vm.CancelTrimCommand.Execute(null);

        Assert.False(vm.IsTrimming);
        Assert.Null(vm.TrimDestructiveWarning);
        Assert.Equal(callsAfterOpen + 1, _host.TrimmingChangedCalls);
    }

    // ---- Moving the trim points ----

    [Fact]
    public void MarkTrimStart_UsesThePlayHeadAndShowsTenths()
    {
        var vm = CreateOpen();
        _host.CurrentPositionSeconds = 12.4;

        vm.MarkTrimStartCommand.Execute(null);

        Assert.Equal("0:12.4", vm.TrimStartDisplay);
        Assert.Equal(12.4 / 120, vm.TrimStartFraction, 6);
    }

    [Fact]
    public void MarkTrimEnd_UsesThePlayHead()
    {
        var vm = CreateOpen();
        _host.CurrentPositionSeconds = 90;

        vm.MarkTrimEndCommand.Execute(null);

        Assert.Equal("1:30.0", vm.TrimEndDisplay);
        Assert.Equal(0.75, vm.TrimEndFraction, 6);
    }

    [Fact]
    public void DraggingAHandle_MovesTheTrimPointAndRaisesItsFraction()
    {
        var vm = CreateOpen();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SetTrimStartFromFraction(0.25);

        Assert.Equal(0.25, vm.TrimStartFraction, 6);
        Assert.Equal("0:30.0", vm.TrimStartDisplay);
        Assert.Contains(nameof(vm.TrimStartFraction), raised);
    }

    [Fact]
    public void DraggingAHandle_ClampsToTheClip()
    {
        var vm = CreateOpen();

        vm.SetTrimStartFromFraction(-0.5);
        Assert.Equal(0, vm.TrimStartFraction, 6);

        vm.SetTrimEndFromFraction(1.5);
        Assert.Equal(1, vm.TrimEndFraction, 6);
    }

    [Fact]
    public void TypingATimestamp_MovesTheTrimPoint()
    {
        var vm = CreateOpen();

        vm.TrimStartDisplay = "0:45";
        vm.CommitTrimStartCommand.Execute(null);

        Assert.Null(vm.TrimStartError);
        Assert.Equal(0.375, vm.TrimStartFraction, 6);
        Assert.Equal("0:45", vm.TrimStartDisplay);
    }

    [Fact]
    public void TypingRubbish_ReportsItAndRestoresTheLastGoodValue()
    {
        var vm = CreateOpen();
        vm.TrimStartDisplay = "0:45";
        vm.CommitTrimStartCommand.Execute(null);

        vm.TrimStartDisplay = "not a time";
        vm.CommitTrimStartCommand.Execute(null);

        Assert.NotNull(vm.TrimStartError);
        Assert.Equal("0:45", vm.TrimStartDisplay);
        Assert.Equal(0.375, vm.TrimStartFraction, 6);
    }

    [Fact]
    public void TypingRubbishIntoTheEndBox_ReportsItAndRestores()
    {
        var vm = CreateOpen();

        vm.TrimEndDisplay = "??";
        vm.CommitTrimEndCommand.Execute(null);

        Assert.NotNull(vm.TrimEndError);
        Assert.Equal("2:00", vm.TrimEndDisplay);
    }

    [Fact]
    public void FractionsAreZero_WhenTheDurationIsNotKnownYet()
    {
        _host.DurationSeconds = 0;
        var vm = CreateOpen();

        Assert.Equal(0, vm.TrimStartFraction);
        Assert.Equal(0, vm.TrimEndFraction);
    }

    // ---- Queueing ----

    [Fact]
    public async Task Queue_DoesNothing_WhenTheRangeIsEmptyOrInverted()
    {
        var vm = CreateOpen();
        vm.SetTrimStartFromFraction(0.8);
        vm.SetTrimEndFromFraction(0.2);

        await vm.QueueTrimExportCommand.ExecuteAsync(null);

        _exportService.Verify(s => s.QueueAsync(
            It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<TrimMode>(),
            It.IsAny<bool>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Queue_DoesNothing_WithoutAnOutputPath()
    {
        var vm = CreateOpen();
        vm.TrimOutputPath = "   ";

        await vm.QueueTrimExportCommand.ExecuteAsync(null);

        _exportService.Verify(s => s.QueueAsync(
            It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<TrimMode>(),
            It.IsAny<bool>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Queue_NonDestructive_QueuesTheJobAndClosesTheForm()
    {
        var vm = CreateOpen();
        vm.SetTrimStartFromFraction(0.25);
        vm.SetTrimEndFromFraction(0.75);

        await vm.QueueTrimExportCommand.ExecuteAsync(null);

        _exportService.Verify(s => s.QueueAsync(
            1, null, vm.TrimOutputPath, TrimMode.NonDestructive, false,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.False(vm.IsTrimming);
        Assert.Equal(1, _host.RunExportQueueCalls);
    }

    [Fact]
    public async Task Queue_Destructive_WithHighlightsInsideTheRange_QueuesWithoutWarning()
    {
        _appSettings.DefaultTrimMode = TrimMode.Destructive;
        _host.HighlightList = [Highlight(1, 40, 50, "Inside")];

        var vm = CreateOpen();
        vm.SetTrimStartFromFraction(0.25); // 30s
        vm.SetTrimEndFromFraction(0.75);   // 90s

        await vm.QueueTrimExportCommand.ExecuteAsync(null);

        Assert.Null(vm.TrimDestructiveWarning);
        _exportService.Verify(s => s.QueueAsync(
            1, null, It.IsAny<string>(), TrimMode.Destructive, true,
            It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Queue_Destructive_WithAClippedHighlight_WarnsInsteadOfQueueing()
    {
        _appSettings.DefaultTrimMode = TrimMode.Destructive;
        _host.HighlightList = [Highlight(1, 5, 15, "Early moment")];

        var vm = CreateOpen();
        vm.SetTrimStartFromFraction(0.25);
        vm.SetTrimEndFromFraction(0.75);

        await vm.QueueTrimExportCommand.ExecuteAsync(null);

        Assert.NotNull(vm.TrimDestructiveWarning);
        Assert.Contains("Early moment", vm.TrimDestructiveWarning);
        Assert.Contains("1 highlight falls outside", vm.TrimDestructiveWarning);
        _exportService.Verify(s => s.QueueAsync(
            It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<TrimMode>(),
            It.IsAny<bool>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Queue_Destructive_WarningPluralisesAndCapsTheNames()
    {
        _appSettings.DefaultTrimMode = TrimMode.Destructive;
        _host.HighlightList =
        [
            Highlight(1, 0, 5, "One"),
            Highlight(2, 0, 5, "Two"),
            Highlight(3, 0, 5, "Three"),
            Highlight(4, 0, 5, "Four"),
        ];

        var vm = CreateOpen();
        vm.SetTrimStartFromFraction(0.25);
        vm.SetTrimEndFromFraction(0.75);

        await vm.QueueTrimExportCommand.ExecuteAsync(null);

        Assert.Contains("4 highlights fall outside", vm.TrimDestructiveWarning);
        Assert.Contains("and 1 more", vm.TrimDestructiveWarning);
    }

    [Fact]
    public async Task ConfirmDestructiveTrim_QueuesAndClearsTheWarning()
    {
        _appSettings.DefaultTrimMode = TrimMode.Destructive;
        _host.HighlightList = [Highlight(1, 5, 15, "Early moment")];

        var vm = CreateOpen();
        vm.SetTrimStartFromFraction(0.25);
        vm.SetTrimEndFromFraction(0.75);
        await vm.QueueTrimExportCommand.ExecuteAsync(null);

        await vm.ConfirmDestructiveTrimCommand.ExecuteAsync(null);

        Assert.Null(vm.TrimDestructiveWarning);
        _exportService.Verify(s => s.QueueAsync(
            1, null, It.IsAny<string>(), TrimMode.Destructive, true,
            It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, _host.RunExportQueueCalls);
    }

    [Fact]
    public async Task Queue_Failure_IsReportedAndTheFormStaysOpen()
    {
        _exportService
            .Setup(s => s.QueueAsync(
                It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<TrimMode>(),
                It.IsAny<bool>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk full"));

        var vm = CreateOpen();
        vm.SetTrimStartFromFraction(0.25);
        vm.SetTrimEndFromFraction(0.75);

        await vm.QueueTrimExportCommand.ExecuteAsync(null);

        Assert.Single(_host.ExportFailures);
        Assert.Contains("disk full", _host.ExportFailures[0]);
        Assert.True(vm.IsTrimming);
        Assert.Equal(0, _host.RunExportQueueCalls);
    }

    // ---- Clip lifecycle ----

    [Fact]
    public void Reset_ClearsTheFormForANewClip()
    {
        var vm = CreateOpen();
        vm.TrimDestructiveWarning = "something";
        vm.TrimStartError = "bad";

        vm.Reset();

        Assert.False(vm.IsTrimming);
        Assert.Equal(string.Empty, vm.TrimOutputPath);
        Assert.Null(vm.TrimDestructiveWarning);
        Assert.Null(vm.TrimStartError);
        Assert.Equal(0, vm.TrimEndFraction, 6);
    }
}
