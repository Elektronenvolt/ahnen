using Ahnen.Desktop.Services;
using Ahnen.Core.Services;
using Microsoft.Extensions.Logging;

namespace Ahnen.Desktop.Tests;

public class PreferencesAndDocumentTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ApplicationLogBuffer _logs = new();
    private readonly ILoggerFactory _factory;

    public PreferencesAndDocumentTests() =>
        _factory = LoggerFactory.Create(b => b.AddProvider(new ApplicationLogProvider(_logs)));

    public void Dispose()
    {
        _factory.Dispose();
        _root.Dispose();
    }

    private PreferencesService Preferences() => new(_root.Paths, _factory.CreateLogger<PreferencesService>());

    private DocumentSession Session() =>
        new(_root.Paths, _factory.CreateLogger<DataStore>(), _factory.CreateLogger<DocumentSession>());

    private void WriteDocument(string contents)
    {
        Directory.CreateDirectory(_root.Paths.DataDirectory);
        File.WriteAllText(Path.Combine(_root.Paths.DataDirectory, DesktopPaths.DocumentFileName), contents);
    }

    [Fact]
    public void Paths_keep_settings_outside_the_data_folder()
    {
        var paths = _root.Paths;

        Assert.Equal(Path.Combine(_root.Path, "data"), paths.DataDirectory);
        Assert.Equal(_root.Path, Path.GetDirectoryName(paths.SettingsFile));
    }

    [Fact]
    public void Missing_settings_give_defaults_without_logging()
    {
        Assert.Null(Preferences().Load().Language);
        Assert.Empty(_logs.Snapshot());
    }

    [Fact]
    public void Saved_preferences_round_trip_and_leave_no_temporary_file()
    {
        var service = Preferences();

        Assert.True(service.TrySave(new Preferences("de")));

        Assert.Equal("de", Preferences().Load().Language);
        Assert.False(File.Exists(_root.Paths.SettingsFile + ".tmp"));
    }

    [Fact]
    public void Malformed_settings_fall_back_to_defaults_and_warn()
    {
        Directory.CreateDirectory(_root.Path);
        File.WriteAllText(_root.Paths.SettingsFile, "{ not json");

        Assert.Null(Preferences().Load().Language);
        Assert.Equal(LogLevel.Warning, Assert.Single(_logs.Snapshot()).Level);
    }

    [Fact]
    public void Unwritable_settings_report_failure_instead_of_throwing()
    {
        Directory.CreateDirectory(_root.Paths.SettingsFile); // a folder where the file should be

        Assert.False(Preferences().TrySave(new Preferences("en")));
        Assert.Contains(_logs.Snapshot(), e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Missing_document_is_read_only_without_an_error()
    {
        var session = Session();

        Assert.False(session.HasDocument);
        Assert.Null(session.LoadError);
        Assert.NotNull(session.Store);
        Assert.True(Directory.Exists(_root.Paths.DataDirectory));
    }

    [Fact]
    public void Existing_document_is_opened_for_editing()
    {
        WriteDocument("{}");

        var session = Session();

        Assert.True(session.HasDocument);
        Assert.Null(session.LoadError);
    }

    [Fact]
    public void Malformed_document_is_reported_and_never_overwritten()
    {
        WriteDocument("invalid json");

        var session = Session();

        Assert.False(session.HasDocument);
        Assert.Null(session.Store);
        Assert.NotNull(session.LoadError);
        Assert.Contains(_logs.Snapshot(), e => e.Level == LogLevel.Error);
        Assert.Equal("invalid json",
            File.ReadAllText(Path.Combine(_root.Paths.DataDirectory, DesktopPaths.DocumentFileName)));
    }
}
