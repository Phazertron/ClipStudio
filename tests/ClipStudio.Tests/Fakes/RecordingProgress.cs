using System.Collections.Generic;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// An <see cref="IProgress{T}"/> that records every report on the thread that made it.
/// </summary>
/// <remarks>
/// Use this in place of <see cref="System.Progress{T}"/> in tests. <see cref="System.Progress{T}"/>
/// dispatches to the captured <see cref="System.Threading.SynchronizationContext"/>, and a test has
/// none - so it falls back to the thread pool and the callbacks run *after* the awaited call has
/// already returned. Asserting on the collected reports is then a race that usually passes, which
/// is the worst kind: `LibraryHealthCheckServiceTests.ReportsProgressPerFolder` failed roughly one
/// run in seven. The same dispatch also meant a plain <see cref="List{T}"/> was being appended to
/// from several pool threads at once.
/// <para>
/// Reporting here is synchronous and locked, so by the time the awaited call returns every report
/// is present and the list is intact.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of progress report.</typeparam>
public sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = [];
    private readonly Lock _gate = new();

    /// <summary>Gets a snapshot of everything reported so far, in order.</summary>
    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (_gate)
                return _reports.ToList();
        }
    }

    /// <inheritdoc/>
    public void Report(T value)
    {
        lock (_gate)
            _reports.Add(value);
    }
}
