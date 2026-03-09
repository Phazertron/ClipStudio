namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides arguments for the <see cref="ILibraryWatcherService.FileDetected"/> event.
/// </summary>
public sealed class FileDetectedEventArgs : EventArgs
{
    /// <summary>Gets the absolute path of the newly detected video file.</summary>
    public string FilePath { get; }

    /// <summary>Gets the identifier of the source folder that contains the detected file.</summary>
    public int SourceFolderId { get; }

    /// <summary>Initializes a new instance of <see cref="FileDetectedEventArgs"/>.</summary>
    public FileDetectedEventArgs(string filePath, int sourceFolderId)
    {
        FilePath = filePath;
        SourceFolderId = sourceFolderId;
    }
}

/// <summary>
/// Watches one or more source folders on disk and raises an event when a new video file is detected.
/// A short stabilisation delay is applied after detection to ensure the file has finished being written.
/// </summary>
public interface ILibraryWatcherService : IDisposable
{
    /// <summary>
    /// Raised when a new video file has been detected in a watched folder and has stabilised on disk.
    /// </summary>
    event EventHandler<FileDetectedEventArgs> FileDetected;

    /// <summary>Begins watching the folder at the given path for new video files.</summary>
    void StartWatching(string folderPath, int sourceFolderId);

    /// <summary>Stops watching the folder at the given path.</summary>
    void StopWatching(string folderPath);

    /// <summary>Stops watching all currently watched folders.</summary>
    void StopAll();
}
