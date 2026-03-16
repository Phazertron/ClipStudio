using System;
using System.Threading.Tasks;
using ClipStudio.Application.Models;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a single tag suggestion chip displayed in the clip detail tag section.
/// Wraps a <see cref="TagSuggestion"/> and exposes display-ready properties plus
/// a command that applies the suggestion to the clip.
/// </summary>
public sealed class TagSuggestionChipViewModel : ViewModelBase
{
    /// <summary>Gets the identifier of the suggested tag.</summary>
    public int TagId { get; }

    /// <summary>Gets the display name of the suggested tag.</summary>
    public string Name { get; }

    /// <summary>Gets the short human-readable reason for the suggestion (e.g. "same game").</summary>
    public string Reason { get; }

    /// <summary>Gets the composite relevance score (higher is better).</summary>
    public int Score { get; }

    /// <summary>
    /// Gets the command that applies this suggestion to the clip.
    /// Wired up by <see cref="ClipDetailViewModel"/> when the chip is created.
    /// </summary>
    public IAsyncRelayCommand AcceptCommand { get; }

    /// <summary>Initialises a new instance of <see cref="TagSuggestionChipViewModel"/>.</summary>
    /// <param name="suggestion">The source suggestion model from the suggestion service.</param>
    /// <param name="acceptAsync">Async callback invoked when the user accepts this suggestion.</param>
    public TagSuggestionChipViewModel(TagSuggestion suggestion, Func<int, Task> acceptAsync)
    {
        TagId         = suggestion.Tag.Id;
        Name          = suggestion.Tag.Name;
        Reason        = suggestion.Reason;
        Score         = suggestion.Score;
        AcceptCommand = new AsyncRelayCommand(() => acceptAsync(TagId));
    }
}
