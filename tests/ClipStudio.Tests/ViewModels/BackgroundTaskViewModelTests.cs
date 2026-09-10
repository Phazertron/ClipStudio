using ClipStudio.Tests.Fakes;
using ClipStudio.UI.ViewModels;
using Material.Icons;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="BackgroundTaskViewModel"/> and the registry that holds them.
/// </summary>
/// <remarks>
/// The registry exists so that what is running is visible from anywhere, and so a finished task's
/// message survives in full - it used to be a single string on a one-line bar that ran off the
/// edge of the window.
/// </remarks>
public sealed class BackgroundTaskViewModelTests
{
    private static BackgroundTaskViewModel Task(
        CancellationTokenSource? cancellation = null,
        Action<BackgroundTaskViewModel>? onFinished = null)
        => new("Doing a thing", MaterialIconKind.Cog, "Settings", cancellation, onFinished);

    [Fact]
    public void StartsRunningAndIndeterminate()
    {
        // Indeterminate until something says otherwise: a repair reports what it is doing rather
        // than how much is left, and showing 0% forever would read as stuck rather than busy.
        var task = Task();

        Assert.True(task.IsRunning);
        Assert.False(task.IsFinished);
        Assert.True(task.IsIndeterminate);
        Assert.Equal("Running", task.StateDisplay);
    }

    [Fact]
    public void ReportingAPercentageMakesItDeterminate()
    {
        var task = Task();

        task.Report(42, "file 42 of 100");

        Assert.False(task.IsIndeterminate);
        Assert.Equal(42, task.ProgressPercent);
        Assert.Equal("file 42 of 100", task.Detail);
    }

    [Fact]
    public void ReportingOnlyADetailLeavesItIndeterminate()
    {
        var task = Task();

        task.Report("hashing clips");

        Assert.True(task.IsIndeterminate);
        Assert.Equal("hashing clips", task.Detail);
    }

    [Theory]
    [InlineData(101, 100)]
    [InlineData(-5, 0)]
    public void ProgressIsClampedToTheBar(double reported, double expected)
    {
        var task = Task();

        task.Report(reported);

        Assert.Equal(expected, task.ProgressPercent);
    }

    [Fact]
    public void FinishingKeepsTheMessageAndClearsTheDetail()
    {
        var task = Task();
        task.Report(50, "half way");

        task.Finish(BackgroundTaskState.Completed, "Sanitize complete: 1 repair, 4 duplicates.");

        Assert.True(task.IsFinished);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Null(task.Detail);
        Assert.Equal("Sanitize complete: 1 repair, 4 duplicates.", task.CompletionMessage);
        Assert.Equal("Finished", task.StateDisplay);
    }

    [Fact]
    public void FinishingTwiceKeepsTheFirstOutcome()
    {
        // A cancelled task that then throws on the way out must not be relabelled as failed.
        var task = Task();
        task.Finish(BackgroundTaskState.Cancelled, "stopped");

        task.Finish(BackgroundTaskState.Failed, "then exploded");

        Assert.Equal(BackgroundTaskState.Cancelled, task.State);
        Assert.Equal("stopped", task.CompletionMessage);
    }

    [Fact]
    public void ATaskWithNoCancellationSourceCannotBeCancelled()
    {
        // Display-only is the honest state for work that cannot be interrupted safely.
        var task = Task();

        Assert.False(task.CanCancel);
        Assert.False(task.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void CancellingRequestsItRatherThanDeclaringItDone()
    {
        // The task decides when it is safe to stop and reports Cancelled itself, so one that has
        // already passed its last cancellation point still finishes normally.
        using var cts = new CancellationTokenSource();
        var task = Task(cts);

        Assert.True(task.CanCancel);
        task.CancelCommand.Execute(null);

        Assert.True(cts.IsCancellationRequested);
        Assert.True(task.IsRunning);
        Assert.Equal("Stopping...", task.Detail);
    }

    [Fact]
    public void AFinishedTaskCannotBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        var task = Task(cts);
        task.Finish(BackgroundTaskState.Completed);

        Assert.False(task.CanCancel);
        Assert.False(cts.IsCancellationRequested);
    }

    // ---- The registry ----

    [Fact]
    public void StartingATaskPutsItInTheRunningList()
    {
        var registry = new FakeBackgroundTaskService();

        var task = registry.Start("Scanning", MaterialIconKind.FolderSearchOutline, "Settings");

        Assert.Single(registry.Running);
        Assert.Empty(registry.Recent);
        Assert.Equal(1, registry.RunningCount);
        Assert.Equal("Settings", task.OwnerNavLabel);
    }

    [Fact]
    public void FinishingMovesItToRecentNewestFirst()
    {
        var registry = new FakeBackgroundTaskService();
        var first  = registry.Start("First",  MaterialIconKind.Cog);
        var second = registry.Start("Second", MaterialIconKind.Cog);

        first.Finish(BackgroundTaskState.Completed, "one");
        second.Finish(BackgroundTaskState.Completed, "two");

        Assert.Empty(registry.Running);
        Assert.Equal(0, registry.RunningCount);
        Assert.Equal("Second", registry.Recent[0].Title);
        Assert.Equal("First",  registry.Recent[1].Title);
    }

    [Fact]
    public void TheRegistryAnnouncesEveryChange()
    {
        // The sidebar indicator and the navigation spinner both follow this one event, so a page
        // that starts work does not also have to remember to spin an icon.
        var registry = new FakeBackgroundTaskService();
        var changes = 0;
        registry.Changed += (_, _) => changes++;

        var task = registry.Start("Scanning", MaterialIconKind.Cog);
        task.Finish(BackgroundTaskState.Completed);

        Assert.Equal(2, changes);
    }
}
