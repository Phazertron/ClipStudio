using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Owns the transport for the clip player: the play head, the duration, the timestamp readouts,
/// scrubbing, relative seeking and the loop mode.
/// </summary>
/// <remarks>
/// <para>
/// The three guards that keep the play head honest all live here, so there is one owner for each:
/// <list type="bullet">
///   <item><c>IsScrubbing</c> - while the user drags the slider, player updates are ignored and the
///   seek is deferred to <see cref="EndScrub"/> rather than flooding the player.</item>
///   <item><c>_isUpdatingFromPlayer</c> - suppresses the seek that
///   <see cref="OnPositionSecondsChanged"/> would otherwise perform when the play head is being
///   written from a player update rather than by the user.</item>
///   <item><c>_ignoreTimeChangedBeforeMs</c> - after a programmatic seek, the player briefly keeps
///   reporting the old position; those stale updates are discarded until one arrives at or past the
///   seek target.</item>
/// </list>
/// </para>
/// <para>
/// A playback <em>window</em> narrows the transport to part of the media: unset for a whole clip,
/// and set to the highlight's range in watch mode. Positions reported to the UI are relative to the
/// window start, so the slider always runs 0..window length. What happens when playback reaches the
/// window end stays with the parent, which owns that policy.
/// </para>
/// </remarks>
public sealed partial class PlaybackViewModel : ViewModelBase
{
    private readonly IPlaybackHost _host;
    private readonly Func<bool> _usePreciseDisplay;

    /// <summary>Suppresses the seek that a play-head write would otherwise trigger.</summary>
    private bool _isUpdatingFromPlayer;

    /// <summary>
    /// After a programmatic seek, player updates reporting a position below this threshold are
    /// discarded as stale. -1 means no seek is in flight.
    /// </summary>
    private long _ignoreTimeChangedBeforeMs = -1;

    /// <summary>The length being displayed: the clip's duration, or the window's when one is set.</summary>
    private TimeSpan _displayedDuration;

    /// <summary>Gets or sets a value indicating whether the player is playing.</summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>
    /// Gets or sets the play head in seconds, relative to the window start when one is set.
    /// Setting this from the UI seeks the player; writes coming from the player do not.
    /// </summary>
    [ObservableProperty]
    private double _positionSeconds;

    /// <summary>Gets or sets the length of the transport in seconds.</summary>
    [ObservableProperty]
    private double _durationSeconds;

    /// <summary>Gets or sets the play head readout.</summary>
    [ObservableProperty]
    private string _positionDisplay = "0:00";

    /// <summary>Gets or sets the duration readout.</summary>
    [ObservableProperty]
    private string _durationDisplay = "0:00";

    /// <summary>Gets or sets how playback repeats.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoopOff))]
    [NotifyPropertyChangedFor(nameof(IsLoopThis))]
    [NotifyPropertyChangedFor(nameof(IsLoopAll))]
    private LoopMode _loopMode = LoopMode.Off;

    /// <summary>Gets a value indicating whether looping is off.</summary>
    public bool IsLoopOff => LoopMode == LoopMode.Off;

    /// <summary>Gets a value indicating whether the current item repeats.</summary>
    public bool IsLoopThis => LoopMode == LoopMode.LoopThis;

    /// <summary>Gets a value indicating whether playback advances through the queue.</summary>
    public bool IsLoopAll => LoopMode == LoopMode.LoopAll;

    /// <summary>Gets a value indicating whether the user is currently dragging the position slider.</summary>
    public bool IsScrubbing { get; private set; }

    /// <summary>Gets the start of the playback window, or null when the whole clip is in play.</summary>
    public TimeSpan? WindowStart { get; private set; }

    /// <summary>Gets the end of the playback window, or null when the whole clip is in play.</summary>
    public TimeSpan? WindowEnd { get; private set; }

    /// <summary>Gets a value indicating whether playback is narrowed to a window.</summary>
    public bool IsWindowed => WindowStart is not null;

    /// <summary>Gets the duration of a single frame, falling back to 30 fps when the rate is unknown.</summary>
    public double FrameDuration => _host.Fps > 0 ? 1.0 / _host.Fps : 1.0 / 30.0;

    /// <summary>Gets the command that toggles between playing and paused.</summary>
    public IRelayCommand PlayPauseCommand { get; }

    /// <summary>Gets the command that jumps back ten seconds.</summary>
    public IRelayCommand SkipBackCommand { get; }

    /// <summary>Gets the command that jumps forward ten seconds.</summary>
    public IRelayCommand SkipForwardCommand { get; }

    /// <summary>Gets the command that steps back one frame.</summary>
    public IRelayCommand FrameBackCommand { get; }

    /// <summary>Gets the command that steps forward one frame.</summary>
    public IRelayCommand FrameForwardCommand { get; }

    /// <summary>Gets the command that cycles the loop mode.</summary>
    public IRelayCommand ToggleRepeatCommand { get; }

    /// <summary>Initialises a new <see cref="PlaybackViewModel"/>.</summary>
    /// <param name="host">The seam onto the media player.</param>
    /// <param name="usePreciseDisplay">
    /// Returns whether timestamps should be shown to a tenth of a second, which the parent turns on
    /// while a trim or highlight range is being edited.
    /// </param>
    public PlaybackViewModel(IPlaybackHost host, Func<bool> usePreciseDisplay)
    {
        _host              = host;
        _usePreciseDisplay = usePreciseDisplay;

        PlayPauseCommand    = new RelayCommand(TogglePlayPause);
        SkipBackCommand     = new RelayCommand(() => SeekRelative(TimeSpan.FromSeconds(-10)));
        SkipForwardCommand  = new RelayCommand(() => SeekRelative(TimeSpan.FromSeconds(10)));
        FrameBackCommand    = new RelayCommand(() => SeekRelative(TimeSpan.FromSeconds(-FrameDuration)));
        FrameForwardCommand = new RelayCommand(() => SeekRelative(TimeSpan.FromSeconds(FrameDuration)));
        ToggleRepeatCommand = new RelayCommand(CycleLoopMode);
    }

    // ---- Setup ----

    /// <summary>
    /// Sets the transport length and refreshes the duration readout.
    /// </summary>
    /// <param name="duration">The length to display: the clip's, or the window's when one is set.</param>
    public void SetDuration(TimeSpan duration)
    {
        _displayedDuration = duration;
        DurationSeconds    = duration.TotalSeconds;
        RefreshDurationDisplay();
    }

    /// <summary>
    /// Narrows the transport to part of the media, or restores it to the whole clip.
    /// </summary>
    /// <param name="start">The window start, or null for the whole clip.</param>
    /// <param name="end">The window end, or null for the whole clip.</param>
    public void SetWindow(TimeSpan? start, TimeSpan? end)
    {
        WindowStart = start;
        WindowEnd   = end;
    }

    /// <summary>Clears the in-flight seek guard. Called when a new clip is loaded.</summary>
    public void Reset()
    {
        IsScrubbing                = false;
        _isUpdatingFromPlayer      = false;
        _ignoreTimeChangedBeforeMs = -1;
    }

    // ---- Transport ----

    /// <summary>Toggles between playing and paused.</summary>
    private void TogglePlayPause()
    {
        if (_host.IsPlayerPlaying)
            _host.Pause();
        else
            _host.Play();
    }

    /// <summary>Cycles Off to LoopThis to LoopAll and back.</summary>
    private void CycleLoopMode() =>
        LoopMode = LoopMode switch
        {
            LoopMode.Off      => LoopMode.LoopThis,
            LoopMode.LoopThis => LoopMode.LoopAll,
            _                 => LoopMode.Off,
        };

    /// <summary>
    /// Moves the play head by <paramref name="offset"/>, clamped to the window when one is set and
    /// to the clip otherwise.
    /// </summary>
    /// <param name="offset">How far to move; negative seeks backwards.</param>
    public void SeekRelative(TimeSpan offset)
    {
        var offsetMs = (long)offset.TotalMilliseconds;

        if (IsWindowed)
        {
            var windowStartMs = (long)WindowStart!.Value.TotalMilliseconds;
            var windowEndMs   = (long)WindowEnd!.Value.TotalMilliseconds;
            var targetMs      = Math.Max(windowStartMs, Math.Min(windowEndMs, _host.TimeMs + offsetMs));

            _host.TimeMs = targetMs;
            UpdatePositionDisplay(TimeSpan.FromMilliseconds(targetMs - windowStartMs));
            return;
        }

        var absoluteMs = Math.Max(0, Math.Min((long)(DurationSeconds * 1000), _host.TimeMs + offsetMs));
        _host.TimeMs = absoluteMs;
        UpdatePositionDisplay(TimeSpan.FromMilliseconds(absoluteMs));
    }

    /// <summary>
    /// Seeks to an absolute position in the media, arming the stale-update guard so the player's
    /// pre-seek reports are discarded.
    /// </summary>
    /// <param name="ms">The target position in milliseconds from the start of the media.</param>
    public void SeekToMs(long ms)
    {
        ArmSeekGuard(ms);
        _host.TimeMs = ms;
    }

    /// <summary>Signals that the user has started dragging the position slider.</summary>
    public void BeginScrub() => IsScrubbing = true;

    /// <summary>
    /// Signals that the user has released the position slider, and seeks to where they let go.
    /// </summary>
    /// <param name="seconds">The target position in seconds, relative to the window start.</param>
    public void EndScrub(double seconds)
    {
        IsScrubbing = false;

        // The slider is window-relative; translate back to an absolute media position.
        var absoluteSeconds = IsWindowed ? WindowStart!.Value.TotalSeconds + seconds : seconds;
        var targetMs        = (long)(absoluteSeconds * 1000);

        ArmSeekGuard(targetMs);
        _host.TimeMs = targetMs;
        UpdatePositionDisplay(TimeSpan.FromSeconds(seconds));
    }

    // ---- Player updates ----

    /// <summary>
    /// Decides whether a player position update should be acted on, and clears the stale-update
    /// guard once a fresh update arrives.
    /// </summary>
    /// <param name="timeMs">The position the player is reporting.</param>
    /// <returns><see langword="true"/> when the update should be ignored.</returns>
    public bool ShouldIgnoreTimeChanged(long timeMs)
    {
        if (IsScrubbing)
            return true;

        if (_ignoreTimeChangedBeforeMs < 0)
            return false;

        if (timeMs < _ignoreTimeChangedBeforeMs)
            return true;

        _ignoreTimeChangedBeforeMs = -1; // the first fresh update arrived; resume normal handling
        return false;
    }

    /// <summary>
    /// Arms the stale-update guard ahead of a seek performed by the parent, which owns the seeks
    /// made from the player's own callbacks.
    /// </summary>
    /// <param name="targetMs">The position being seeked to.</param>
    public void ArmSeekGuard(long targetMs) => _ignoreTimeChangedBeforeMs = targetMs - 200;

    /// <summary>
    /// Writes the play head and readout from a known position without seeking back to it.
    /// Also used while the player is paused, when no position updates arrive.
    /// </summary>
    /// <param name="ts">The position to show, relative to the window start.</param>
    public void UpdatePositionDisplay(TimeSpan ts)
    {
        _isUpdatingFromPlayer = true;
        PositionSeconds = ts.TotalSeconds;
        PositionDisplay = Format(ts);
        _isUpdatingFromPlayer = false;
    }

    /// <summary>
    /// Writes the play head from an absolute player position, converting it to window-relative
    /// when a window is set.
    /// </summary>
    /// <param name="absolute">The player's reported position.</param>
    public void UpdatePositionFromPlayer(TimeSpan absolute)
    {
        var relative = IsWindowed
            ? (absolute > WindowStart!.Value ? absolute - WindowStart.Value : TimeSpan.Zero)
            : absolute;

        UpdatePositionDisplay(relative);
    }

    /// <summary>
    /// Reformats the duration readout, after the parent switches precision on or off.
    /// </summary>
    /// <remarks>
    /// The play-head readout is deliberately left alone, matching the behaviour from before this
    /// transport was extracted: it takes on the new precision at the next player update. That
    /// leaves it stale while the player is paused; see the entry in PLAN.md.
    /// </remarks>
    public void RefreshDurationDisplay() => DurationDisplay = Format(_displayedDuration);

    /// <summary>
    /// Called by the source generator when <see cref="PositionSeconds"/> changes. A change the user
    /// made seeks the player; one written from a player update or mid-drag does not.
    /// </summary>
    /// <remarks>
    /// The value is treated as an absolute media position even when a window is set. That is wrong
    /// for a windowed clip and is carried over unchanged from before this transport was extracted,
    /// so the extraction stays behaviour-only; see the entry in PLAN.md. Dragging the slider is
    /// unaffected, because a drag is suppressed here and settled by <see cref="EndScrub"/>, which
    /// does convert.
    /// </remarks>
    /// <param name="value">The new play-head position in seconds.</param>
    partial void OnPositionSecondsChanged(double value)
    {
        if (_isUpdatingFromPlayer || IsScrubbing)
            return;

        SeekToMs((long)(value * 1000));
    }

    /// <summary>Formats a timestamp at the precision the parent currently wants.</summary>
    /// <param name="ts">The value to format.</param>
    /// <returns>The formatted timestamp.</returns>
    private string Format(TimeSpan ts) =>
        _usePreciseDisplay() ? FormatPrecise(ts) : FormatPlain(ts);

    /// <summary>Formats a timestamp to the second.</summary>
    /// <param name="ts">The value to format.</param>
    /// <returns>The formatted timestamp.</returns>
    public static string FormatPlain(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

    /// <summary>Formats a timestamp to a tenth of a second.</summary>
    /// <param name="ts">The value to format.</param>
    /// <returns>The formatted timestamp.</returns>
    public static string FormatPrecise(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss\.f") : ts.ToString(@"m\:ss\.f");
}
