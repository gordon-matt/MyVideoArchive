namespace MyVideoArchive.Services.Content;

/// <summary>
/// Forwards to the caller's logger. The shared video helpers are typed on their own category,
/// and the download job already has a logger in hand.
/// </summary>
internal sealed class LoggerAdapter<T>(ILogger inner) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        inner.Log(logLevel, eventId, state, exception, formatter);
}