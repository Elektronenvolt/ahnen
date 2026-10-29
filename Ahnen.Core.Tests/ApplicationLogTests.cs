using Ahnen.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ahnen.Core.Tests;

public class ApplicationLogTests
{
    [Fact]
    public void Provider_respects_configuration_and_keeps_other_targets()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Ahnen"] = "Information",
            ["Logging:LogLevel:Disabled"] = "None"
        }).Build();
        var buffer = new ApplicationLogBuffer();
        var otherTarget = new ApplicationLogBuffer();
        using var factory = LoggerFactory.Create(builder => builder
            .AddConfiguration(config.GetSection("Logging"))
            .AddProvider(new ApplicationLogProvider(buffer))
            .AddProvider(new ApplicationLogProvider(otherTarget)));

        var logger = factory.CreateLogger("Ahnen.Test");
        logger.LogDebug("Filtered");
        logger.LogInformation(new EventId(42, "TestEvent"), new InvalidOperationException("Details"),
            "Loaded {FileName}", "test.json");
        factory.CreateLogger("Framework").LogInformation("Filtered category");
        factory.CreateLogger("Framework").LogWarning("Visible warning");
        factory.CreateLogger("Disabled").LogCritical("Filtered entirely");

        var entries = buffer.Snapshot();
        Assert.Equal(2, entries.Count);
        Assert.Equal("Loaded test.json", entries[0].Message);
        Assert.Equal(LogLevel.Information, entries[0].Level);
        Assert.Equal("Ahnen.Test", entries[0].Category);
        Assert.Equal(new EventId(42, "TestEvent"), entries[0].EventId);
        Assert.Contains("InvalidOperationException: Details", entries[0].Exception);
        Assert.True(entries[0].Timestamp <= DateTimeOffset.UtcNow);
        Assert.Equal("Visible warning", entries[1].Message);
        Assert.Equal(entries.Select(e => e.Message), otherTarget.Snapshot().Select(e => e.Message));
    }

    [Fact]
    public void Viewer_specific_filter_uses_provider_alias()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:ApplicationLogs:LogLevel:Default"] = "Error"
        }).Build();
        var buffer = new ApplicationLogBuffer();
        using var factory = LoggerFactory.Create(builder => builder
            .AddConfiguration(config.GetSection("Logging"))
            .AddProvider(new ApplicationLogProvider(buffer)));

        var logger = factory.CreateLogger("Test");
        logger.LogWarning("Hidden in viewer");
        logger.LogError("Visible error");

        Assert.Equal("Visible error", Assert.Single(buffer.Snapshot()).Message);
    }

    [Fact]
    public void Buffer_retains_only_latest_500_entries_in_sequence_under_concurrency()
    {
        var buffer = new ApplicationLogBuffer();
        Parallel.For(0, 1000, i => buffer.Append(LogLevel.Warning, "Concurrent", i, $"Entry {i}", null));

        var entries = buffer.Snapshot();
        Assert.Equal(500, entries.Count);
        Assert.Equal(Enumerable.Range(501, 500).Select(i => (long)i), entries.Select(e => e.Sequence));
        Assert.Equal(1000, buffer.Version);
        buffer.Append(LogLevel.Error, "After", 0, "New entry", null);
        Assert.Equal(501, entries[0].Sequence);
        Assert.Equal(502, buffer.Snapshot()[0].Sequence);
        Assert.Equal("New entry", buffer.Snapshot()[^1].Message);
    }

    [Fact]
    public void None_is_never_recorded()
    {
        var buffer = new ApplicationLogBuffer();
        using var provider = new ApplicationLogProvider(buffer);
        var logger = provider.CreateLogger("Test");
        Assert.False(logger.IsEnabled(LogLevel.None));
        logger.Log(LogLevel.None, "Not a log entry");
        Assert.Empty(buffer.Snapshot());
    }

}
