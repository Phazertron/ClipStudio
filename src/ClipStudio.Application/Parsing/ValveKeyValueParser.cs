using System.Text;

namespace ClipStudio.Application.Parsing;

/// <summary>
/// Reads Valve's KeyValues text format, which Steam uses for both <c>libraryfolders.vdf</c> and
/// the per-game <c>appmanifest_*.acf</c> files.
/// </summary>
/// <remarks>
/// The format is quoted keys, each followed by either a quoted value or a brace-delimited block:
/// <code>
/// "AppState"
/// {
///     "appid"  "10180"
///     "name"   "Call of Duty: Modern Warfare 2 (2009)"
/// }
/// </code>
/// Only what is needed to read those two files is implemented: quoted strings with backslash
/// escapes, nested blocks, and <c>//</c> comments. Conditional tokens such as <c>[$WIN32]</c> and
/// unquoted keys appear in other Valve files but not in these, so they are not handled - a parser
/// that pretended to support them without being exercised against any would be worse than one that
/// is honest about its scope.
/// </remarks>
public static class ValveKeyValueParser
{
    /// <summary>
    /// Parses a KeyValues document into a tree.
    /// </summary>
    /// <param name="text">The document's contents.</param>
    /// <returns>
    /// The root node. A document with one top-level block, as both Steam files have, yields a root
    /// whose single child is that block.
    /// </returns>
    public static ValveKeyValueNode Parse(string text)
    {
        var root  = new ValveKeyValueNode();
        var index = 0;

        while (true)
        {
            SkipWhitespaceAndComments(text, ref index);
            if (index >= text.Length) break;

            if (!TryReadString(text, ref index, out var key)) break;

            SkipWhitespaceAndComments(text, ref index);
            if (index >= text.Length) break;

            if (text[index] == '{')
            {
                index++;
                root.Children[key] = ParseBlock(text, ref index);
            }
            else if (TryReadString(text, ref index, out var value))
            {
                root.Values[key] = value;
            }
        }

        return root;
    }

    /// <summary>Parses the contents of a block, consuming its closing brace.</summary>
    /// <param name="text">The document.</param>
    /// <param name="index">The position just past the opening brace; left just past the closing one.</param>
    /// <returns>The block as a node.</returns>
    private static ValveKeyValueNode ParseBlock(string text, ref int index)
    {
        var node = new ValveKeyValueNode();

        while (true)
        {
            SkipWhitespaceAndComments(text, ref index);
            if (index >= text.Length) break;

            if (text[index] == '}')
            {
                index++;
                break;
            }

            if (!TryReadString(text, ref index, out var key)) break;

            SkipWhitespaceAndComments(text, ref index);
            if (index >= text.Length) break;

            if (text[index] == '{')
            {
                index++;
                node.Children[key] = ParseBlock(text, ref index);
            }
            else if (TryReadString(text, ref index, out var value))
            {
                node.Values[key] = value;
            }
        }

        return node;
    }

    /// <summary>Advances past whitespace and <c>//</c> comments.</summary>
    /// <param name="text">The document.</param>
    /// <param name="index">The position to advance.</param>
    private static void SkipWhitespaceAndComments(string text, ref int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
            }
            else if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] is not ('\n' or '\r'))
                    index++;
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>Reads one quoted string, resolving backslash escapes.</summary>
    /// <param name="text">The document.</param>
    /// <param name="index">The position of the opening quote; left just past the closing one.</param>
    /// <param name="value">The string read.</param>
    /// <returns>Whether a string was read.</returns>
    private static bool TryReadString(string text, ref int index, out string value)
    {
        value = string.Empty;
        if (index >= text.Length || text[index] != '"') return false;

        index++;
        var builder = new StringBuilder();

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '\\' && index + 1 < text.Length)
            {
                // Paths in libraryfolders.vdf are escaped, so "C:\\Program Files" has to come back
                // as a usable path rather than a doubled separator.
                index++;
                builder.Append(text[index] switch
                {
                    'n'  => '\n',
                    't'  => '\t',
                    '\\' => '\\',
                    '"'  => '"',
                    var other => other,
                });
                index++;
                continue;
            }

            if (c == '"')
            {
                index++;
                value = builder.ToString();
                return true;
            }

            builder.Append(c);
            index++;
        }

        // Unterminated string: take what there was rather than throwing. A truncated manifest
        // should cost one game, not the whole scan.
        value = builder.ToString();
        return true;
    }
}
