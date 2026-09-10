using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.Services;
using Moq;

namespace ClipStudio.Tests.Services;

/// <summary>
/// Unit tests for <see cref="UpdateCheckScheduler"/>, which keeps looking for releases while the
/// application runs.
/// </summary>
public sealed class UpdateCheckSchedulerTests
{
    private readonly FakeApplicationUpdateService _updates = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _current = new();

    public UpdateCheckSchedulerTests()
        => _settings.Setup(s => s.Current).Returns(_current);

    private UpdateCheckScheduler Build() => new(_updates, _settings.Object);

    /// <summary>
    /// Waits for a condition rather than sleeping a fixed time, so the test is neither slow nor
    /// dependent on how quickly the first pass gets scheduled.
    /// </summary>
    private static async Task<bool> WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10);

        return condition();
    }

    [Fact]
    public async Task ChecksOnceAsSoonAsItStarts()
    {
        _current.AutomaticUpdateChecksEnabled = true;

        await using var scheduler = Build();
        scheduler.Start();

        Assert.True(await WaitFor(() => _updates.CheckCount >= 1), "Expected a check on start.");
    }

    [Fact]
    public async Task ChecksNothingWhenAutomaticChecksAreTurnedOff()
    {
        _current.AutomaticUpdateChecksEnabled = false;

        await using var scheduler = Build();
        scheduler.Start();

        // Give it the same room the enabled case needs before concluding it stayed quiet.
        await Task.Delay(150);

        Assert.Equal(0, _updates.CheckCount);
    }

    [Fact]
    public async Task StartingTwiceDoesNotRunTwoLoops()
    {
        _current.AutomaticUpdateChecksEnabled = true;

        await using var scheduler = Build();
        scheduler.Start();
        scheduler.Start();

        Assert.True(await WaitFor(() => _updates.CheckCount >= 1));
        await Task.Delay(100);

        // The interval is hours, so a second check inside this window could only come from a
        // second loop.
        Assert.Equal(1, _updates.CheckCount);
    }

    [Fact]
    public async Task DisposingStopsTheLoop()
    {
        _current.AutomaticUpdateChecksEnabled = true;

        var scheduler = Build();
        scheduler.Start();
        Assert.True(await WaitFor(() => _updates.CheckCount >= 1));

        await scheduler.DisposeAsync();

        var countAtDispose = _updates.CheckCount;
        await Task.Delay(100);

        Assert.Equal(countAtDispose, _updates.CheckCount);
    }

    [Fact]
    public void ChecksOftenEnoughToBeUsefulInALongSession()
    {
        // The whole point of the scheduler: a session that runs for days must hear about a
        // release without being restarted.
        Assert.True(UpdateCheckScheduler.Interval <= TimeSpan.FromHours(12));
        Assert.True(UpdateCheckScheduler.Interval >= TimeSpan.FromMinutes(30));
    }
}
