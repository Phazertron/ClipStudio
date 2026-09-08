using ClipStudio.Core.Entities;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for the clip detail view so <see cref="HighlightEditorViewModel"/> can be driven
/// on its own.
/// </summary>
public sealed class FakeHighlightEditorHost : IHighlightEditorHost
{
    /// <inheritdoc/>
    public Clip? CurrentClip { get; set; }

    /// <inheritdoc/>
    public double CurrentPositionSeconds { get; set; }

    /// <inheritdoc/>
    public double DurationSeconds { get; set; }

    /// <summary>Gets the number of times the trim form was asked to close.</summary>
    public int PrepareCalls { get; private set; }

    /// <summary>Gets the number of times the editor reported an open/close/switch.</summary>
    public int EditingStateChangedCalls { get; private set; }

    /// <summary>Gets the number of times a highlight creation was reported.</summary>
    public int HighlightCreatedCalls { get; private set; }

    /// <inheritdoc/>
    public void PrepareForHighlightEdit() => PrepareCalls++;

    /// <inheritdoc/>
    public void OnEditingStateChanged() => EditingStateChangedCalls++;

    /// <inheritdoc/>
    public Task OnHighlightCreatedAsync()
    {
        HighlightCreatedCalls++;
        return Task.CompletedTask;
    }
}
