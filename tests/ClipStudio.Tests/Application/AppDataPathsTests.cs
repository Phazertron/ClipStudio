using ClipStudio.Application.Models;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Covers the resolved application data paths, and in particular the rule that a disposable
/// <c>--profile</c> launch must not write anything into the user's real folders.
/// </summary>
public class AppDataPathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ClipStudioPathsTest");

    [Fact]
    public void CacheFoldersAlwaysSitUnderTheRoot()
    {
        var paths = new AppDataPaths(Root);

        Assert.Equal(Path.Combine(Root, "audio_cache"), paths.AudioCachePath);
        Assert.Equal(Path.Combine(Root, "media-cache"), paths.MediaCachePath);
        Assert.Equal(Root, paths.Root);
    }

    [Fact]
    public void WithoutAProfileScreenshotsDefaultToThePicturesFolder()
    {
        var paths = new AppDataPaths(Root);

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "ClipStudio");

        Assert.False(paths.IsProfileScoped);
        Assert.Equal(expected, paths.DefaultScreenshotFolder);
    }

    [Fact]
    public void UnderAProfileScreenshotsStayInsideTheProfileRoot()
    {
        var paths = new AppDataPaths(Root, isProfileScoped: true);

        Assert.True(paths.IsProfileScoped);
        Assert.Equal(Path.Combine(Root, "screenshots"), paths.DefaultScreenshotFolder);
        Assert.StartsWith(Root, paths.DefaultScreenshotFolder);
    }

    [Fact]
    public void AProfileWritesNothingOutsideItsOwnRoot()
    {
        var paths = new AppDataPaths(Root, isProfileScoped: true);

        // The point of --profile is that deleting the root undoes the whole run.
        foreach (var written in new[]
                 {
                     paths.AudioCachePath,
                     paths.MediaCachePath,
                     paths.DefaultScreenshotFolder,
                 })
        {
            Assert.StartsWith(Root, written);
        }
    }
}
