using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ahnen.Desktop.Services;

/// <summary>User preferences. Never part of the genealogy document.</summary>
public sealed record Preferences(string? Language = null,
    Visualization.TreeChart Chart = Visualization.TreeChart.Hourglass,
    Visualization.TreeColors Colors = Visualization.TreeColors.Sex,
    Visualization.TreeOrientation Orientation = Visualization.TreeOrientation.Vertical,
    int Generations = 4, bool ShowIds = false, bool ShowSex = true);

/// <summary>Reads and writes <see cref="Preferences"/>; problems are logged and never block startup.</summary>
public sealed class PreferencesService(DesktopPaths paths, ILogger<PreferencesService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Preferences Load()
    {
        try
        {
            if (!File.Exists(paths.SettingsFile)) return new Preferences();
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(paths.SettingsFile), JsonOptions)
                ?? new Preferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Settings file {SettingsFile} could not be read; using defaults.", paths.SettingsFile);
            return new Preferences();
        }
    }

    /// <summary>Returns false (after logging) when the preferences could not be persisted.</summary>
    public bool TrySave(Preferences preferences)
    {
        var tmp = paths.SettingsFile + ".tmp";
        try
        {
            Directory.CreateDirectory(paths.Root);
            File.WriteAllText(tmp, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(tmp, paths.SettingsFile, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Settings file {SettingsFile} could not be written.", paths.SettingsFile);
            try { File.Delete(tmp); } catch { /* best effort */ }
            return false;
        }
    }
}
