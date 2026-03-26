namespace ClipStudio.Core.Enums;

/// <summary>
/// Specifies the hardware backend used by the Whisper inference engine during transcription.
/// </summary>
public enum TranscriptionBackend
{
    /// <summary>
    /// Let the runtime automatically select the fastest available backend,
    /// preferring Vulkan when present and falling back to CPU.
    /// </summary>
    Auto,

    /// <summary>
    /// Force CPU-only inference. Works on all machines with no additional drivers.
    /// Slower than GPU-accelerated backends.
    /// </summary>
    Cpu,

    /// <summary>
    /// Use Vulkan GPU acceleration. Supported on AMD, Intel, and NVIDIA graphics cards
    /// without requiring CUDA. Requires a Vulkan-capable driver, which is typically
    /// installed by default with modern GPU drivers.
    /// </summary>
    Vulkan
}
