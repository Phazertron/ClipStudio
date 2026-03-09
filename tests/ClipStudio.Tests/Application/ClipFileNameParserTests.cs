using ClipStudio.Application.Parsing;
using Xunit;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ClipFileNameParser"/>.
/// </summary>
public sealed class ClipFileNameParserTests
{
    // ---- ExtractGameName ----

    [Fact]
    public void ExtractGameName_StandardObsScriptName_ReturnsGameTitle()
    {
        var result = ClipFileNameParser.ExtractGameName("Replay 2025-03-03 22-49-45 [Apex Legends].mp4");
        Assert.Equal("Apex Legends", result);
    }

    [Fact]
    public void ExtractGameName_NoGameName_ReturnsNull()
    {
        var result = ClipFileNameParser.ExtractGameName("Replay 2025-03-03 22-49-45.mp4");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractGameName_GameNameWithSpecialChars_ReturnsTrimmedValue()
    {
        var result = ClipFileNameParser.ExtractGameName("Replay 2025-01-01 12-00-00 [ELDEN RING].mkv");
        Assert.Equal("ELDEN RING", result);
    }

    [Fact]
    public void ExtractGameName_MultipleBrackets_ReturnsLastOne()
    {
        // The OBS script only writes one bracket token; if there are multiple the last wins.
        var result = ClipFileNameParser.ExtractGameName("Some [old] Replay [Valorant].mp4");
        Assert.Equal("Valorant", result);
    }

    [Fact]
    public void ExtractGameName_EmptyBrackets_ReturnsNull()
    {
        // Empty brackets contain no characters; the parser requires at least one character inside.
        var result = ClipFileNameParser.ExtractGameName("Replay 2025-01-01 12-00-00 [].mp4");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractGameName_PathProvided_ParsesFromFileName()
    {
        var result = ClipFileNameParser.ExtractGameName(
            @"C:\Users\user\Videos\Replay 2025-03-03 22-49-45 [Cyberpunk 2077].mp4");
        Assert.Equal("Cyberpunk 2077", result);
    }

    // ---- ExtractTimestamp ----

    [Fact]
    public void ExtractTimestamp_ValidObsName_ReturnsCorrectDateTime()
    {
        var result = ClipFileNameParser.ExtractTimestamp("Replay 2025-03-03 22-49-45.mp4");
        Assert.NotNull(result);
        Assert.Equal(2025, result!.Value.Year);
        Assert.Equal(3, result.Value.Month);
        Assert.Equal(3, result.Value.Day);
        Assert.Equal(22, result.Value.Hour);
        Assert.Equal(49, result.Value.Minute);
        Assert.Equal(45, result.Value.Second);
        Assert.Equal(DateTimeKind.Unspecified, result.Value.Kind);
    }

    [Fact]
    public void ExtractTimestamp_NoTimestamp_ReturnsNull()
    {
        var result = ClipFileNameParser.ExtractTimestamp("myclip.mp4");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractTimestamp_WithGameBracket_StillParsesCorrectly()
    {
        var result = ClipFileNameParser.ExtractTimestamp("Replay 2024-11-15 08-30-00 [Minecraft].mkv");
        Assert.NotNull(result);
        Assert.Equal(2024, result!.Value.Year);
        Assert.Equal(11, result.Value.Month);
        Assert.Equal(15, result.Value.Day);
    }

    // ---- IsSupportedVideoFile ----

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.mkv")]
    [InlineData("clip.mov")]
    [InlineData("clip.webm")]
    [InlineData("clip.avi")]
    [InlineData("clip.flv")]
    [InlineData("clip.ts")]
    [InlineData("CLIP.MP4")]
    public void IsSupportedVideoFile_KnownExtension_ReturnsTrue(string fileName)
    {
        Assert.True(ClipFileNameParser.IsSupportedVideoFile(fileName));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("document.pdf")]
    [InlineData("audio.mp3")]
    [InlineData("noextension")]
    public void IsSupportedVideoFile_UnsupportedExtension_ReturnsFalse(string fileName)
    {
        Assert.False(ClipFileNameParser.IsSupportedVideoFile(fileName));
    }
}
