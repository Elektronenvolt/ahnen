namespace Ahnen.Web.Services;

[ProviderAlias("ApplicationLogs")]
public sealed class ApplicationLogProvider(ApplicationLogBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new BufferLogger(buffer, categoryName);

    public void Dispose() { }

    private sealed class BufferLogger(ApplicationLogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                buffer.Append(logLevel, category, eventId, formatter(state, exception), exception);
        }
    }
}
