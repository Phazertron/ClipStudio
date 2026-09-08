using ClipStudio.Core.Entities;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for the clip detail view so <see cref="TrimEditorViewModel"/> can be driven on its own.
/// </summary>
public sealed class FakeTrimEditorHost : ITrimEditorHost
{
    /// <inheritdoc/>
    public Clip? CurrentClip { get; set; }

    /// <inheritdoc/>
    public bool CanBeginTrim { get; set; } = true;

    /// <inheritdoc/>
    public double CurrentPositionSeconds { get; set; }

    /// <inheritdoc/>
    public double DurationSeconds { get; set; }

    /// <summary>Gets or sets the highlights the destructive-trim check sees.</summary>
    public List<HighlightViewModel> HighlightList { get; set; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<HighlightViewModel> Highlights => HighlightList;

    /// <summary>Gets the number of times the highlight form was asked to close.</summary>
    public int PrepareForTrimCalls { get; private set; }

    /// <summary>Gets the number of times the trim form reported opening or closing.</summary>
    public int TrimmingChangedCalls { get; private set; }

    /// <summary>Gets the number of times the export queue was asked to run.</summary>
    public int RunExportQueueCalls { get; private set; }

    /// <summary>Gets the export failures reported, in order.</summary>
    public List<string> ExportFailures { get; } = [];

    /// <inheritdoc/>
    public void PrepareForTrim() => PrepareForTrimCalls++;

    /// <inheritdoc/>
    public void OnTrimmingChanged() => TrimmingChangedCalls++;

    /// <inheritdoc/>
    public void RunExportQueue() => RunExportQueueCalls++;

    /// <inheritdoc/>
    public void ReportExportFailure(string message) => ExportFailures.Add(message);
}
