using System.IO;
using Ahnen.Core.Services;
using Microsoft.Extensions.Logging;

namespace Ahnen.Desktop.Services;

/// <summary>
/// Opens the desktop document from the per-user data folder. A missing document leaves the store
/// read-only (import is a later step); an unreadable one is reported instead of crashing startup.
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
            Store = new DataStore(new DataStoreOptions(paths.DataDirectory, DesktopPaths.DocumentFileName), storeLogger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            LoadError = ex.Message;
            logger.LogError(ex, "The document in {DataDirectory} could not be loaded.", paths.DataDirectory);
        }
    }
}
