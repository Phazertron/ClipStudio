using Microsoft.Extensions.Logging;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Records the messages a service logs, so a test can assert on a decision the output does not
/// otherwise reveal.
/// </summary>
/// <typeparam name="T">The category the logger belongs to.</typeparam>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    /// <summary>Gets the formatted messages logged so far, in order.</summary>
    public IReadOnlyList<string> Messages
    {
        get { lock (_messages) return _messages.ToList(); }
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_messages) _messages.Add(formatter(state, exception));
    }
}
