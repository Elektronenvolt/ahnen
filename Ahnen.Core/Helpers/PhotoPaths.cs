namespace Ahnen.Core.Helpers;

public static class PhotoPaths
{
    /// <summary>Thumbnail path convention for a stored photo (relative to the data directory).</summary>
    public static string ThumbPath(string mainFile)
    {
        var dir = Path.GetDirectoryName(mainFile)?.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(mainFile);
        var thumb = $"{name}_thumb.webp";
        return string.IsNullOrEmpty(dir) ? thumb : $"{dir}/{thumb}";
    }
}
