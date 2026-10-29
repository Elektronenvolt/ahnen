using System.IO;

namespace Ahnen.Desktop.Services;

/// <summary>
/// Genealogy records and configuration live beside the application; user preferences stay separate.
/// </summary>
public sealed class DesktopPaths(string root, string? applicationDirectory = null)
{
    public const string DocumentFileName = "familytree.json";

    public string Root { get; } = root;
    public string DataDirectory { get; } = applicationDirectory ?? AppContext.BaseDirectory;
    public string ConfigurationFile => Path.Combine(DataDirectory, "appsettings.json");
    public string SettingsFile { get; } = Path.Combine(root, "settings.json");

    /// <summary>The <c>AHNEN_DESKTOP_ROOT</c> environment variable overrides only the user preferences location.</summary>
    public static DesktopPaths ForCurrentUser() =>
        new(Environment.GetEnvironmentVariable("AHNEN_DESKTOP_ROOT") is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ahnen"));
}
