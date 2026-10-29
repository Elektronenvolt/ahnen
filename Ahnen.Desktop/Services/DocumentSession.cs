using System.IO;
using System.Text.Json;
using Ahnen.Core.Services;
using Microsoft.Extensions.Logging;

namespace Ahnen.Desktop.Services;

/// <summary>
/// Opens the configured document beside the application. Missing data leaves the store read-only;
/// unreadable data or configuration is reported instead of crashing startup.
/// </summary>
public sealed class DocumentSession
{
    public DataStore? Store { get; }
    public string? LoadError { get; }
    public bool HasDocument => Store?.CanEdit == true;

    public DocumentSession(DesktopPaths paths, ILogger<DataStore> storeLogger, ILogger<DocumentSession> logger)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<DesktopConfiguration>(File.ReadAllText(paths.ConfigurationFile))
                ?? throw new JsonException("The desktop configuration must contain an object.");
            Store = new DataStore(new DataStoreOptions(paths.DataDirectory, configuration.DataFileName), storeLogger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadError = ex.Message;
            logger.LogError(ex, "The configuration {ConfigurationFile} or document in {DataDirectory} could not be loaded.",
                paths.ConfigurationFile, paths.DataDirectory);
        }
    }

    private sealed record DesktopConfiguration(string? DataFileName);
}
