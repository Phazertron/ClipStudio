using ClipStudio.Application.Parsing;

namespace ClipStudio.Tests.Parsing;

/// <summary>
/// Unit tests for <see cref="ValveKeyValueParser"/>, which reads Steam's <c>.vdf</c> and
/// <c>.acf</c> files.
/// </summary>
/// <remarks>
/// The shapes here are taken from real files on a machine with 248 installed titles across two
/// library folders, rather than from the format documentation.
/// </remarks>
public sealed class ValveKeyValueParserTests
{
    [Fact]
    public void ReadsAScalarOutOfABlock()
    {
        var node = ValveKeyValueParser.Parse("""
            "AppState"
            {
                "appid"  "10180"
                "name"   "Call of Duty: Modern Warfare 2 (2009)"
            }
            """);

        var state = node.Child("AppState");

        Assert.NotNull(state);
        Assert.Equal("10180", state.Value("appid"));
        Assert.Equal("Call of Duty: Modern Warfare 2 (2009)", state.Value("name"));
    }

    [Fact]
    public void KeysAreCaseInsensitive()
    {
        // Steam is not consistent about casing between files or client versions.
        var node = ValveKeyValueParser.Parse("""
            "AppState"
            {
                "AppID"  "440"
            }
            """);

        Assert.Equal("440", node.Child("appstate")?.Value("appid"));
    }

    [Fact]
    public void ReadsNestedBlocks()
    {
        var node = ValveKeyValueParser.Parse("""
            "libraryfolders"
            {
                "0"
                {
                    "path"  "C:\\Program Files (x86)\\Steam"
                    "apps"
                    {
                        "228980"  "1084871972"
                    }
                }
            }
            """);

        var entry = node.Child("libraryfolders")?.Child("0");

        Assert.NotNull(entry);
        Assert.Equal(@"C:\Program Files (x86)\Steam", entry.Value("path"));
        Assert.Equal("1084871972", entry.Child("apps")?.Value("228980"));
    }

    [Fact]
    public void UnescapesBackslashesInPaths()
    {
        // Paths arrive escaped, and a doubled separator is not a usable path.
        var node = ValveKeyValueParser.Parse("""
            "libraryfolders"
            {
                "1"
                {
                    "path"  "G:\\SteamLibrary"
                }
            }
            """);

        Assert.Equal(@"G:\SteamLibrary", node.Child("libraryfolders")?.Child("1")?.Value("path"));
    }

    [Fact]
    public void IgnoresComments()
    {
        var node = ValveKeyValueParser.Parse("""
            // written by Steam
            "AppState"
            {
                // the app
                "appid"  "10180"
            }
            """);

        Assert.Equal("10180", node.Child("AppState")?.Value("appid"));
    }

    [Fact]
    public void AnEmptyDocumentYieldsAnEmptyNode()
    {
        var node = ValveKeyValueParser.Parse(string.Empty);

        Assert.Empty(node.Values);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void ATruncatedFileYieldsWhatItCanRatherThanThrowing()
    {
        // A manifest cut short mid-write should cost one game, not the whole scan.
        var node = ValveKeyValueParser.Parse("""
            "AppState"
            {
                "appid"  "10180"
                "name"   "Half a nam
            """);

        Assert.Equal("10180", node.Child("AppState")?.Value("appid"));
    }

    [Fact]
    public void MissingKeysComeBackNullRatherThanThrowing()
    {
        var node = ValveKeyValueParser.Parse("""
            "AppState"
            {
                "appid"  "10180"
            }
            """);

        Assert.Null(node.Child("AppState")?.Value("name"));
        Assert.Null(node.Child("NoSuchBlock"));
    }
}
