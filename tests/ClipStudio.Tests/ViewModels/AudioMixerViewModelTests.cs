using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.ViewModels;
using Material.Icons;
using Moq;
using Xunit;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="AudioMixerViewModel"/> covering master volume and mute, track
/// discovery, the choice between native track selection and an FFmpeg remux, and the preview
/// cache slots.
/// </summary>
public sealed class AudioMixerViewModelTests
{
    private const string ClipPath = "/clips/Replay.mp4";
    private const string DataRoot = "/appdata/ClipStudio";

    private readonly FakeAudioPlaybackHost _host = new();
    private readonly Mock<IAudioTrackService> _audioTracks = new();
    private readonly Mock<IMixedAudioService> _mixedAudio = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly FakeFileSystem _fileSystem = new();
    private readonly AppSettings _appSettings = new();
    private readonly AppDataPaths _paths = new(DataRoot);

    private readonly Clip _clip = new()
    {
        Id = 1,
        FilePath = ClipPath,
        FileName = "Replay.mp4"
    };

    /// <summary>Sets up a mixer with two discovered tracks and no cached preview.</summary>
    public AudioMixerViewModelTests()
    {
        _settings.Setup(s => s.Current).Returns(_appSettings);
        _host.WithTrack(0, "Desktop").WithTrack(1, "Mic");

        _audioTracks
            .Setup(s => s.GetOrInitAsync(It.IsAny<int>(), It.IsAny<IEnumerable<(int, string)>>()))
            .ReturnsAsync([
                new AudioTrackSetting { ClipId = 1, TrackIndex = 0, DisplayName = "Desktop", Volume = 1.0 },
                new AudioTrackSetting { ClipId = 1, TrackIndex = 1, DisplayName = "Mic",     Volume = 1.0 },
            ]);

        _mixedAudio
            .Setup(m => m.GenerateRemuxAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<TrackMixInfo>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<TrackMixInfo> _, string output, CancellationToken _) => output);
    }

    /// <summary>Builds the mixer under test.</summary>
    /// <returns>A new mixer wired to the fakes.</returns>
    private AudioMixerViewModel CreateMixer() => new(
        _host,
        _audioTracks.Object,
        _mixedAudio.Object,
        _settings.Object,
        _fileSystem,
        _paths);

    /// <summary>Builds a mixer already pointed at the test clip.</summary>
    /// <returns>A mixer with the clip set.</returns>
    private AudioMixerViewModel CreateMixerForClip()
    {
        var mixer = CreateMixer();
        mixer.SetClip(_clip);
        return mixer;
    }

    // ---- Master volume and mute ----

    [Fact]
    public void Constructor_AppliesTheInitialVolumeToThePlayer()
    {
        // The field initialiser bypasses the generated setter, so without the explicit apply the
        // player keeps whatever the system VLC config supplies, which can be silence.
        var mixer = CreateMixer();

        Assert.Equal(100, mixer.MasterVolume);
        Assert.Equal(100, _host.Volume);
    }

    [Fact]
    public void MasterVolume_IsAppliedToThePlayer()
    {
        var mixer = CreateMixer();

        mixer.MasterVolume = 40;

        Assert.Equal(40, _host.Volume);
    }

    [Fact]
    public void MasterVolume_IsNotAppliedWhileMuted()
    {
        var mixer = CreateMixer();
        mixer.IsMuted = true;

        mixer.MasterVolume = 40;

        Assert.Equal(0, _host.Volume);
    }

    [Fact]
    public void Unmuting_RestoresTheMasterVolume()
    {
        var mixer = CreateMixer();
        mixer.MasterVolume = 65;

        mixer.IsMuted = true;
        Assert.Equal(0, _host.Volume);

        mixer.IsMuted = false;
        Assert.Equal(65, _host.Volume);
    }

    [Fact]
    public void ToggleMuteCommand_FlipsMuteBothWays()
    {
        var mixer = CreateMixer();

        mixer.ToggleMuteCommand.Execute(null);
        Assert.True(mixer.IsMuted);

        mixer.ToggleMuteCommand.Execute(null);
        Assert.False(mixer.IsMuted);
    }

    [Fact]
    public void SetMasterVolumeToFullCommand_SnapsBackTo100()
    {
        var mixer = CreateMixer();
        mixer.MasterVolume = 12;

        mixer.SetMasterVolumeToFullCommand.Execute(null);

        Assert.Equal(100, mixer.MasterVolume);
        Assert.Equal(100, _host.Volume);
    }

    [Theory]
    [InlineData(0, false, MaterialIconKind.VolumeMute)]
    [InlineData(50, true, MaterialIconKind.VolumeMute)]
    [InlineData(30, false, MaterialIconKind.VolumeLow)]
    [InlineData(80, false, MaterialIconKind.VolumeMedium)]
    [InlineData(150, false, MaterialIconKind.VolumeHigh)]
    public void VolumeIconKind_ReflectsLevelAndMute(int volume, bool muted, MaterialIconKind expected)
    {
        var mixer = CreateMixer();
        mixer.MasterVolume = volume;
        mixer.IsMuted = muted;

        Assert.Equal(expected, mixer.VolumeIconKind);
    }

    // ---- Track discovery ----

    [Fact]
    public async Task RefreshAudioTracks_WithoutAClip_DoesNothing()
    {
        var mixer = CreateMixer();

        await mixer.RefreshAudioTracksAsync();

        Assert.False(mixer.HasTracks);
    }

    [Fact]
    public async Task RefreshAudioTracks_BuildsOneEntryPerPlayerTrack()
    {
        var mixer = CreateMixerForClip();

        await mixer.RefreshAudioTracksAsync();

        Assert.True(mixer.HasTracks);
        Assert.Equal(2, mixer.AudioTracks.Count);
        Assert.Equal(["Desktop", "Mic"], mixer.AudioTracks.Select(t => t.DisplayName));
        Assert.All(mixer.AudioTracks, t => Assert.True(t.IsIncluded));
    }

    [Fact]
    public async Task RefreshAudioTracks_MapsFfmpegStreamIndexByPlayerOrder()
    {
        // The player reports non-contiguous ids; the FFmpeg index is the position in that list.
        _host.Tracks.Clear();
        _host.WithTrack(3, "Desktop").WithTrack(7, "Mic");

        _audioTracks
            .Setup(s => s.GetOrInitAsync(It.IsAny<int>(), It.IsAny<IEnumerable<(int, string)>>()))
            .ReturnsAsync([
                new AudioTrackSetting { ClipId = 1, TrackIndex = 7, DisplayName = "Mic",     Volume = 1.0 },
                new AudioTrackSetting { ClipId = 1, TrackIndex = 3, DisplayName = "Desktop", Volume = 1.0 },
            ]);

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        var mic     = mixer.AudioTracks.Single(t => t.TrackIndex == 7);
        var desktop = mixer.AudioTracks.Single(t => t.TrackIndex == 3);

        Assert.Equal(1, mic.FfmpegStreamIndex);
        Assert.Equal(0, desktop.FfmpegStreamIndex);
    }

    [Fact]
    public async Task RefreshAudioTracks_PublishesTrackNames()
    {
        var mixer = CreateMixerForClip();
        IReadOnlyList<string>? published = null;
        mixer.TracksRefreshed += names => published = names;

        await mixer.RefreshAudioTracksAsync();

        Assert.Equal(["Desktop", "Mic"], published!);
    }

    [Fact]
    public async Task RefreshAudioTracks_NoPlayerTracksYet_LeavesTheListEmpty()
    {
        _host.Tracks.Clear();
        var mixer = CreateMixerForClip();

        await mixer.RefreshAudioTracksAsync();

        Assert.False(mixer.HasTracks);
        _audioTracks.Verify(
            s => s.GetOrInitAsync(It.IsAny<int>(), It.IsAny<IEnumerable<(int, string)>>()), Times.Never);
    }

    // ---- Initial-load routing rules ----

    [Fact]
    public async Task InitialLoad_DoesNotSelectATrackNatively()
    {
        // Calling SetAudioTrack from inside the player's Playing callback silently breaks audio
        // output on Windows. This guard is the whole reason isInitialLoad exists.
        _mixedAudio.Setup(m => m.ShouldUseMix(It.IsAny<IEnumerable<TrackMixInfo>>())).Returns(false);

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        Assert.Empty(_host.SetAudioTrackCalls);
        Assert.Empty(_host.ReloadCalls);
    }

    [Fact]
    public async Task NoIncludedTracks_SilencesThePlayerEvenOnInitialLoad()
    {
        _audioTracks
            .Setup(s => s.GetOrInitAsync(It.IsAny<int>(), It.IsAny<IEnumerable<(int, string)>>()))
            .ReturnsAsync([
                new AudioTrackSetting { ClipId = 1, TrackIndex = 0, DisplayName = "Desktop", IsMuted = true },
                new AudioTrackSetting { ClipId = 1, TrackIndex = 1, DisplayName = "Mic",     IsMuted = true },
            ]);

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        Assert.Equal([-1], _host.SetAudioTrackCalls);
        Assert.False(mixer.IsPlayingMixPreview);
        Assert.False(mixer.HasPendingAudioChanges);
    }

    [Fact]
    public async Task InitialLoad_NeedingAMix_GeneratesItInTheBackground()
    {
        _mixedAudio.Setup(m => m.ShouldUseMix(It.IsAny<IEnumerable<TrackMixInfo>>())).Returns(true);

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        // The generation is fire-and-forget; give it a moment to complete.
        await WaitUntil(() => mixer.IsPlayingMixPreview);

        Assert.Single(_host.ReloadCalls);
        Assert.EndsWith("clip_1_audio_preview_alt.mkv", _host.ReloadCalls[0].Path);
        Assert.True(mixer.IsPlayingMixPreview);
    }

    [Fact]
    public async Task InitialLoad_WithACachedMix_AdoptsItInsteadOfRegenerating()
    {
        _mixedAudio.Setup(m => m.ShouldUseMix(It.IsAny<IEnumerable<TrackMixInfo>>())).Returns(true);
        _appSettings.CacheAudioPreviews = true;
        _fileSystem.AddFile($"{DataRoot}/audio_cache/clip_1_audio_preview.mkv");

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        Assert.Single(_host.ReloadCalls);
        Assert.EndsWith("clip_1_audio_preview.mkv", _host.ReloadCalls[0].Path);
        Assert.True(mixer.IsPlayingMixPreview);
        _mixedAudio.Verify(m => m.GenerateRemuxAsync(
            It.IsAny<string>(), It.IsAny<IEnumerable<TrackMixInfo>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitialLoad_WithBothSlotsCached_PrefersTheNewerFile()
    {
        _mixedAudio.Setup(m => m.ShouldUseMix(It.IsAny<IEnumerable<TrackMixInfo>>())).Returns(true);
        _appSettings.CacheAudioPreviews = true;

        _fileSystem.AddFile(
            $"{DataRoot}/audio_cache/clip_1_audio_preview.mkv",
            lastWriteTimeUtc: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _fileSystem.AddFile(
            $"{DataRoot}/audio_cache/clip_1_audio_preview_alt.mkv",
            lastWriteTimeUtc: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        Assert.EndsWith("clip_1_audio_preview_alt.mkv", _host.ReloadCalls[0].Path);
    }

    [Fact]
    public async Task InitialLoad_CachingDisabled_IgnoresAnExistingPreview()
    {
        _mixedAudio.Setup(m => m.ShouldUseMix(It.IsAny<IEnumerable<TrackMixInfo>>())).Returns(true);
        _appSettings.CacheAudioPreviews = false;
        _fileSystem.AddFile($"{DataRoot}/audio_cache/clip_1_audio_preview.mkv");

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();
        await WaitUntil(() => mixer.IsPlayingMixPreview);

        _mixedAudio.Verify(m => m.GenerateRemuxAsync(
            It.IsAny<string>(), It.IsAny<IEnumerable<TrackMixInfo>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Apply Mix ----

    [Fact]
    public async Task ApplyAudioMix_WritesToTheSlotThePlayerIsNotOn()
    {
        _mixedAudio.Setup(m => m.ShouldUseMix(It.IsAny<IEnumerable<TrackMixInfo>>())).Returns(true);
        _appSettings.CacheAudioPreviews = true;
        _fileSystem.AddFile($"{DataRoot}/audio_cache/clip_1_audio_preview.mkv");

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        // Adopted slot 0; the next generation must target slot 1 so FFmpeg never writes to the
        // file the player currently holds open.
        await mixer.ApplyAudioMixCommand.ExecuteAsync(null);

        Assert.EndsWith("clip_1_audio_preview_alt.mkv", _host.ReloadCalls[^1].Path);
        Assert.True(mixer.IsPlayingMixPreview);
        Assert.False(mixer.IsMixApplying);
    }

    [Fact]
    public async Task ApplyAudioMix_Failure_RestoresThePendingFlagAndAKnownGoodTrack()
    {
        _mixedAudio.Setup(m => m.ShouldUseMix(It.IsAny<IEnumerable<TrackMixInfo>>())).Returns(false);
        _mixedAudio
            .Setup(m => m.GenerateRemuxAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<TrackMixInfo>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg exploded"));

        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        await mixer.ApplyAudioMixCommand.ExecuteAsync(null);

        Assert.True(mixer.HasPendingAudioChanges);
        Assert.False(mixer.IsMixApplying);
        Assert.Equal(0, _host.SetAudioTrackCalls[^1]);
    }

    [Fact]
    public async Task ApplyAudioMix_WithoutTracks_DoesNothing()
    {
        var mixer = CreateMixerForClip();

        await mixer.ApplyAudioMixCommand.ExecuteAsync(null);

        Assert.Empty(_host.ReloadCalls);
        Assert.False(mixer.IsMixApplying);
    }

    // ---- Settings persistence ----

    [Fact]
    public async Task SaveAudioSettings_PersistsIncludeAndVolumePerTrack()
    {
        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();

        mixer.AudioTracks[1].IsIncluded = false;
        mixer.AudioTracks[0].Volume = 0.5;

        IEnumerable<AudioTrackSetting>? saved = null;
        _audioTracks
            .Setup(s => s.SaveAsync(1, It.IsAny<IEnumerable<AudioTrackSetting>>()))
            .Callback<int, IEnumerable<AudioTrackSetting>>((_, settings) => saved = settings.ToList())
            .Returns(Task.CompletedTask);

        await mixer.SaveAudioSettingsCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        var list = saved!.ToList();
        Assert.Equal(0.5, list[0].Volume);
        Assert.False(list[0].IsMuted);
        Assert.True(list[1].IsMuted);
    }

    // ---- Clip lifecycle and preview cleanup ----

    [Fact]
    public async Task SetClip_ClearsPerClipState()
    {
        _mixedAudio.Setup(m => m.ShouldUseMix(It.IsAny<IEnumerable<TrackMixInfo>>())).Returns(true);
        var mixer = CreateMixerForClip();
        await mixer.RefreshAudioTracksAsync();
        await WaitUntil(() => mixer.IsPlayingMixPreview);

        mixer.SetClip(new Clip { Id = 2, FilePath = "/clips/Other.mp4", FileName = "Other.mp4" });

        Assert.False(mixer.HasTracks);
        Assert.Empty(mixer.AudioTracks);
        Assert.False(mixer.IsPlayingMixPreview);
        Assert.False(mixer.HasPendingAudioChanges);
        Assert.False(mixer.IsMixApplying);
    }

    [Fact]
    public void DeleteSessionPreviews_RemovesBothSlots_WhenCachingIsOff()
    {
        _appSettings.CacheAudioPreviews = false;
        _fileSystem
            .AddFile($"{DataRoot}/audio_cache/clip_1_audio_preview.mkv")
            .AddFile($"{DataRoot}/audio_cache/clip_1_audio_preview_alt.mkv");

        CreateMixerForClip().DeleteSessionPreviews();

        Assert.False(_fileSystem.FileExists($"{DataRoot}/audio_cache/clip_1_audio_preview.mkv"));
        Assert.False(_fileSystem.FileExists($"{DataRoot}/audio_cache/clip_1_audio_preview_alt.mkv"));
    }

    [Fact]
    public void DeleteSessionPreviews_KeepsFiles_WhenCachingIsOn()
    {
        _appSettings.CacheAudioPreviews = true;
        _fileSystem.AddFile($"{DataRoot}/audio_cache/clip_1_audio_preview.mkv");

        CreateMixerForClip().DeleteSessionPreviews();

        Assert.True(_fileSystem.FileExists($"{DataRoot}/audio_cache/clip_1_audio_preview.mkv"));
    }

    [Fact]
    public void DeleteSessionPreviews_WithoutAClip_DoesNothing()
    {
        _appSettings.CacheAudioPreviews = false;

        CreateMixer().DeleteSessionPreviews();

        Assert.Empty(_fileSystem.AllFiles);
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds, so tests can wait on the fire-and-forget
    /// mix generation without sleeping for a fixed period.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <returns>A task that completes once the condition holds or the timeout elapses.</returns>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10);
    }
}
