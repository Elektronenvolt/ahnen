namespace Ahnen.Desktop.Tests;

/// <summary>A throw-away per-test directory standing in for the per-user desktop root.</summary>
public sealed class TempRoot : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ahnen-desktop-tests-" + Guid.NewGuid().ToString("N"));

    public Ahnen.Desktop.Services.DesktopPaths Paths => new(Path, System.IO.Path.Combine(Path, "application"));

    public TempRoot()
    {
        Directory.CreateDirectory(Paths.DataDirectory);
        File.WriteAllText(Paths.ConfigurationFile, "{\"DataFileName\":\"familytree.json\"}");
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
