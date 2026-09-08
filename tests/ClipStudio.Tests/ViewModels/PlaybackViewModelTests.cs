using ClipStudio.Tests.Fakes;
using ClipStudio.UI.ViewModels;
using Xunit;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="PlaybackViewModel"/> covering the transport, the three play-head
/// guards, timestamp precision, and the windowed (watch-mode) arithmetic.
/// </summary>
public sealed class PlaybackViewModelTests
{
    private readonly FakePlaybackHost _host = new();
    private bool _precise;

    /// <summary>Builds the transport under test.</summary>
    /// <returns>A transport wired to the fake host.</returns>
    private PlaybackViewModel Create() => new(_host, () => _precise);

    /// <summary>Builds a transport for a whole clip of the given length.</summary>
    /// <param name="seconds">The clip length in seconds.</param>
    /// <returns>A transport with its duration set.</returns>
    private PlaybackViewModel CreateForClip(double seconds = 120)
    {
        var vm = Create();
        vm.SetDuration(TimeSpan.FromSeconds(seconds));
        return vm;
    }

    // ---- Transport ----

    [Fact]
    public void PlayPause_StartsWhenStoppedAndPausesWhenPlaying()
    {
        var vm = Create();

        vm.PlayPauseCommand.Execute(null);
        Assert.Equal(1, _host.PlayCalls);

        vm.PlayPauseCommand.Execute(null);
        Assert.Equal(1, _host.PauseCalls);
    }

    [Fact]
    public void ToggleRepeat_CyclesOffThisAllAndBack()
    {
        var vm = Create();
        Assert.True(vm.IsLoopOff);

        vm.ToggleRepeatCommand.Execute(null);
        Assert.True(vm.IsLoopThis);

        vm.ToggleRepeatCommand.Execute(null);
        Assert.True(vm.IsLoopAll);

        vm.ToggleRepeatCommand.Execute(null);
        Assert.True(vm.IsLoopOff);
    }

    [Fact]
    public void LoopMode_NotifiesTheDerivedFlags()
    {
        var vm = Create();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.LoopMode = LoopMode.LoopAll;

        Assert.Contains(nameof(vm.IsLoopOff), raised);
        Assert.Contains(nameof(vm.IsLoopThis), raised);
        Assert.Contains(nameof(vm.IsLoopAll), raised);
    }

    [Fact]
    public void FrameDuration_FallsBackTo30FpsWhenTheRateIsUnknown()
    {
        var vm = Create();

        _host.Fps = 0;
        Assert.Equal(1.0 / 30.0, vm.FrameDuration, 6);

        _host.Fps = 60;
        Assert.Equal(1.0 / 60.0, vm.FrameDuration, 6);
    }

    [Fact]
    public void SkipForward_MovesTenSecondsAndClampsToTheClipEnd()
    {
        var vm = CreateForClip(seconds: 30);
        _host.TimeMs = 5_000;

        vm.SkipForwardCommand.Execute(null);
        Assert.Equal(15_000, _host.TimeMs);

        vm.SkipForwardCommand.Execute(null);
        vm.SkipForwardCommand.Execute(null);
        Assert.Equal(30_000, _host.TimeMs);
    }

    [Fact]
    public void SkipBack_ClampsToTheStart()
    {
        var vm = CreateForClip();
        _host.TimeMs = 3_000;

        vm.SkipBackCommand.Execute(null);

        Assert.Equal(0, _host.TimeMs);
    }

    [Fact]
    public void SeekRelative_UpdatesTheReadoutWithoutWaitingForThePlayer()
    {
        var vm = CreateForClip();
        _host.TimeMs = 20_000;

        vm.SeekRelative(TimeSpan.FromSeconds(10));

        Assert.Equal(30, vm.PositionSeconds, 3);
        Assert.Equal("0:30", vm.PositionDisplay);
    }

    // ---- Play-head guards ----

    [Fact]
    public void SettingPositionFromTheUi_SeeksThePlayer()
    {
        var vm = CreateForClip();

        vm.PositionSeconds = 42;

        Assert.Equal(42_000, _host.TimeMs);
    }

    [Fact]
    public void PositionWrittenFromThePlayer_DoesNotSeekBack()
    {
        var vm = CreateForClip();
        _host.TimeMs = 1_234;

        vm.UpdatePositionFromPlayer(TimeSpan.FromSeconds(42));

        // A seek here would fight the player and stutter playback.
        Assert.Equal(1_234, _host.TimeMs);
        Assert.Equal(42, vm.PositionSeconds, 3);
    }

    [Fact]
    public void WhileScrubbing_PlayerUpdatesAreIgnoredAndNoSeekIsIssued()
    {
        var vm = CreateForClip();
        vm.BeginScrub();

        Assert.True(vm.ShouldIgnoreTimeChanged(5_000));

        vm.PositionSeconds = 60;
        Assert.Equal(0, _host.TimeMs);
    }

    [Fact]
    public void EndScrub_SeeksToWhereTheUserLetGo()
    {
        var vm = CreateForClip();
        vm.BeginScrub();

        vm.EndScrub(75);

        Assert.False(vm.IsScrubbing);
        Assert.Equal(75_000, _host.TimeMs);
        Assert.Equal(75, vm.PositionSeconds, 3);
    }

    [Fact]
    public void AfterASeek_StalePlayerUpdatesAreDiscardedUntilAFreshOneArrives()
    {
        var vm = CreateForClip();
        vm.SeekToMs(60_000);

        // The player keeps reporting the old position for a moment.
        Assert.True(vm.ShouldIgnoreTimeChanged(10_000));
        Assert.True(vm.ShouldIgnoreTimeChanged(59_000));

        // Then one arrives at the target and normal handling resumes.
        Assert.False(vm.ShouldIgnoreTimeChanged(59_900));
        Assert.False(vm.ShouldIgnoreTimeChanged(1_000));
    }

    [Fact]
    public void ArmSeekGuard_LetsTheParentSuppressItsOwnSeeks()
    {
        var vm = CreateForClip();

        vm.ArmSeekGuard(30_000);

        Assert.True(vm.ShouldIgnoreTimeChanged(1_000));
        Assert.False(vm.ShouldIgnoreTimeChanged(29_800));
    }

    [Fact]
    public void Reset_ClearsTheGuards()
    {
        var vm = CreateForClip();
        vm.BeginScrub();
        vm.ArmSeekGuard(60_000);

        vm.Reset();

        Assert.False(vm.IsScrubbing);
        Assert.False(vm.ShouldIgnoreTimeChanged(0));
    }

    // ---- Timestamp precision ----

    [Fact]
    public void Readouts_UsePlainFormatByDefault()
    {
        var vm = CreateForClip(seconds: 95);

        vm.UpdatePositionDisplay(TimeSpan.FromSeconds(63.4));

        Assert.Equal("1:03", vm.PositionDisplay);
        Assert.Equal("1:35", vm.DurationDisplay);
    }

    [Fact]
    public void Readouts_UsePreciseFormatWhileEditing()
    {
        _precise = true;
        var vm = CreateForClip(seconds: 95);

        vm.UpdatePositionDisplay(TimeSpan.FromSeconds(63.4));

        Assert.Equal("1:03.4", vm.PositionDisplay);
        Assert.Equal("1:35.0", vm.DurationDisplay);
    }

    [Fact]
    public void RefreshDurationDisplay_ReformatsOnlyTheDuration()
    {
        var vm = CreateForClip(seconds: 95);
        vm.UpdatePositionDisplay(TimeSpan.FromSeconds(63.4));

        _precise = true;
        vm.RefreshDurationDisplay();

        Assert.Equal("1:35.0", vm.DurationDisplay);
        // Carried over from before the extraction: the play head takes on the new precision at the
        // next player update rather than immediately.
        Assert.Equal("1:03", vm.PositionDisplay);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void FormatPlain_RendersToTheSecond(double seconds, string expected)
        => Assert.Equal(expected, PlaybackViewModel.FormatPlain(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(65.45, "1:05.4")]
    [InlineData(3725.5, "1:02:05.5")]
    public void FormatPrecise_RendersToATenth(double seconds, string expected)
        => Assert.Equal(expected, PlaybackViewModel.FormatPrecise(TimeSpan.FromSeconds(seconds)));

    // ---- Windowed playback (watch mode) ----

    [Fact]
    public void SetWindow_NarrowsPlaybackAndIsReversible()
    {
        var vm = CreateForClip();
        Assert.False(vm.IsWindowed);

        vm.SetWindow(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45));
        Assert.True(vm.IsWindowed);

        vm.SetWindow(null, null);
        Assert.False(vm.IsWindowed);
    }

    [Fact]
    public void Windowed_PositionIsReportedRelativeToTheWindowStart()
    {
        var vm = CreateForClip();
        vm.SetWindow(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45));

        vm.UpdatePositionFromPlayer(TimeSpan.FromSeconds(34));

        Assert.Equal(4, vm.PositionSeconds, 3);
    }

    [Fact]
    public void Windowed_PositionBeforeTheWindowStartClampsToZero()
    {
        var vm = CreateForClip();
        vm.SetWindow(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45));

        vm.UpdatePositionFromPlayer(TimeSpan.FromSeconds(12));

        Assert.Equal(0, vm.PositionSeconds, 3);
    }

    [Fact]
    public void Windowed_SeekRelativeStaysInsideTheWindow()
    {
        var vm = CreateForClip();
        vm.SetWindow(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45));
        _host.TimeMs = 40_000;

        vm.SeekForwardPastWindowEnd();
        Assert.Equal(45_000, _host.TimeMs);

        vm.SeekRelative(TimeSpan.FromSeconds(-60));
        Assert.Equal(30_000, _host.TimeMs);
    }

    [Fact]
    public void Windowed_EndScrubTranslatesBackToAnAbsolutePosition()
    {
        var vm = CreateForClip();
        vm.SetWindow(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45));
        vm.BeginScrub();

        vm.EndScrub(5);

        Assert.Equal(35_000, _host.TimeMs);
        Assert.Equal(5, vm.PositionSeconds, 3);
    }
}

/// <summary>Small helpers that keep the window tests readable.</summary>
internal static class PlaybackViewModelTestExtensions
{
    /// <summary>Seeks far enough forward to run past the end of any window.</summary>
    /// <param name="vm">The transport to seek.</param>
    public static void SeekForwardPastWindowEnd(this PlaybackViewModel vm)
        => vm.SeekRelative(TimeSpan.FromSeconds(60));
}
