using System.IO;

namespace Ahnen.Desktop.Services;

/// <summary>
/// Per-user locations. Genealogy records live under <see cref="DataDirectory"/>; settings stay
/// beside it so that replacing or importing a document never touches preferences.
/// </summary>
public sealed class DesktopPaths(string root)
{
    public const string DocumentFileName = "familytree.json";

    public string Root { get; } = root;
    public string DataDirectory { get; } = Path.Combine(root, "data");
    public string SettingsFile { get; } = Path.Combine(root, "settings.json");

    /// <summary>The <c>AHNEN_DESKTOP_ROOT</c> environment variable overrides the location (tests, portable use).</summary>
    public static DesktopPaths ForCurrentUser() =>
        new(Environment.GetEnvironmentVariable("AHNEN_DESKTOP_ROOT") is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ahnen"));
}
