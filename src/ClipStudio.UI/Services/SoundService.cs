using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Services;

/// <summary>
/// Plays short UI sound effects by generating minimal PCM WAV data in memory and
/// routing it through the platform audio API.
/// On Windows, <see cref="System.Media.SoundPlayer"/> is used.
/// On other platforms, audio playback is a no-op until a cross-platform back-end is wired.
/// </summary>
public sealed class SoundService : ISoundService
{
    private readonly ISettingsService _settingsService;

    /// <summary>
    /// Initialises a new <see cref="SoundService"/>.
    /// </summary>
    /// <param name="settingsService">
    /// Settings service used to check whether sound effects are enabled before each play call.
    /// </param>
    public SoundService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc/>
    public void Play(SoundEffect effect)
    {
        if (!_settingsService.Current.SoundEffectsEnabled)
            return;

        try
        {
            var wav = BuildWav(effect);
            // Play synchronously on a background thread so the UI is never blocked.
            _ = Task.Run(() =>
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                        PlaySoundWindows(wav);
                    // Cross-platform back-end deferred to a future revision.
                }
                catch { /* non-fatal */ }
            });
        }
        catch
        {
            // Sound playback is non-fatal; silently ignore any errors.
        }
    }

    // ---- WAV generation ----

    /// <summary>
    /// Generates a minimal in-memory PCM WAV byte array for the requested effect.
    /// Each effect is defined by one or more (frequency Hz, duration ms, amplitude 0–1) segments
    /// that are concatenated into a single waveform.
    /// </summary>
    private static byte[] BuildWav(SoundEffect effect)
    {
        // All effects use 44100 Hz, 16-bit mono.
        const int sampleRate  = 44_100;
        const int bitsPerSample = 16;
        const int channels    = 1;

        (double freq, int durationMs, double amplitude)[] segments = effect switch
        {
            SoundEffect.HighlightCreated => new[]
            {
                // Single soft tick: 880 Hz, 90 ms, quiet
                (880.0, 90, 0.25),
            },

            SoundEffect.ImportComplete => new[]
            {
                // Ascending two-tone: C5→E5, 140 ms each
                (523.25, 140, 0.30),
                (659.25, 160, 0.30),
            },

            SoundEffect.ClipTrashed => new[]
            {
                // Descending tone: A4→E4, 130 ms each
                (440.0, 130, 0.25),
                (329.63, 150, 0.20),
            },

            SoundEffect.ExportComplete => new[]
            {
                // Success three-tone: C5→E5→G5, 120 ms each
                (523.25, 120, 0.30),
                (659.25, 120, 0.30),
                (783.99, 150, 0.30),
            },

            _ => new[] { (440.0, 100, 0.20) },
        };

        // Build PCM sample array.
        int totalSamples = 0;
        foreach (var (_, ms, _) in segments)
            totalSamples += (int)(sampleRate * ms / 1000.0);

        var pcm = new short[totalSamples];
        int offset = 0;

        foreach (var (freq, durationMs, amplitude) in segments)
        {
            int count = (int)(sampleRate * durationMs / 1000.0);
            double twoPiF = 2.0 * Math.PI * freq;

            for (int i = 0; i < count; i++)
            {
                double t   = (double)i / sampleRate;
                // Apply a short fade-out in the last 20 % of each segment to avoid clicks.
                double env = i < count * 0.8 ? 1.0 : (count - i) / (count * 0.2);
                double sample = amplitude * env * Math.Sin(twoPiF * t);
                pcm[offset + i] = (short)(sample * short.MaxValue);
            }

            offset += count;
        }

        // Write RIFF WAV header + data.
        int dataBytes   = totalSamples * (bitsPerSample / 8) * channels;
        int blockAlign  = channels * (bitsPerSample / 8);
        int byteRate    = sampleRate * blockAlign;

        using var ms2 = new MemoryStream(44 + dataBytes);
        using var w   = new BinaryWriter(ms2);

        // RIFF chunk
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataBytes);          // chunk size
        w.Write(new[] { 'W', 'A', 'V', 'E' });

        // fmt sub-chunk
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);                      // sub-chunk size (PCM)
        w.Write((short)1);                // PCM format
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bitsPerSample);

        // data sub-chunk
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataBytes);
        foreach (var s in pcm)
            w.Write(s);

        return ms2.ToArray();
    }

    // ---- Windows audio P/Invoke ----

    // winmm.dll PlaySound flags
    private const uint SndSync     = 0x0000;
    private const uint SndMemory   = 0x0004;
    private const uint SndNoDefault = 0x0002;

    [DllImport("winmm.dll", EntryPoint = "PlaySound", SetLastError = false)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool NativePlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

    /// <summary>
    /// Plays a WAV byte array on Windows via the native <c>winmm!PlaySound</c> API.
    /// Runs synchronously; call from a background thread to avoid blocking the UI.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void PlaySoundWindows(byte[] wav)
        => NativePlaySound(wav, IntPtr.Zero, SndMemory | SndSync | SndNoDefault);
}
