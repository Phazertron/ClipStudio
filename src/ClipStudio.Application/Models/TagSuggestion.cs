using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Models;

/// <summary>
/// Represents a tag recommended for a clip by <see cref="Interfaces.ITagSuggestionService"/>.
/// </summary>
public sealed class TagSuggestion
{
    /// <summary>Gets the recommended tag.</summary>
    public Tag Tag { get; }

    /// <summary>Gets a short human-readable explanation of why this tag is suggested.</summary>
    public string Reason { get; }

    /// <summary>Gets the computed relevance score used for ordering (higher is better).</summary>
    public int Score { get; }

    /// <summary>Initialises a new instance of <see cref="TagSuggestion"/>.</summary>
    public TagSuggestion(Tag tag, string reason, int score)
    {
        Tag    = tag;
        Reason = reason;
        Score  = score;
    }
}
