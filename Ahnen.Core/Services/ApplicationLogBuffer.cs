namespace Ahnen.Core.Services;

public sealed record ApplicationLogEntry(
    long Sequence, DateTimeOffset Timestamp, LogLevel Level, string Category,
    EventId EventId, string Message, string? Exception);

public sealed class ApplicationLogBuffer
{
    public const int Capacity = 500;
    private readonly object _gate = new();
    private readonly Queue<ApplicationLogEntry> _entries = new();
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public IReadOnlyList<ApplicationLogEntry> Snapshot()
    {
        lock (_gate)
            return _entries.ToArray();
    }

    public void Append(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        lock (_gate)
        {
            var entry = new ApplicationLogEntry(++_version, DateTimeOffset.UtcNow, level, category,
                eventId, message, exception?.ToString());
            _entries.Enqueue(entry);
            if (_entries.Count > Capacity)
                _entries.Dequeue();
        }
    }
}
