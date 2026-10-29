using Ahnen.Web.Helpers;
using Ahnen.Web.Models;
using SkiaSharp;

namespace Ahnen.Web.Services;

/// <summary>
/// Stores person photos as files under data/photos. Uploads are re-encoded
/// (strips EXIF and potential payloads), downscaled to a sane size, and get a
/// square thumbnail for tree cards. The JSON only ever stores the relative path.
/// </summary>
public class PhotoService(DataStore store)
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int MaxEdge = 1600;
    private const int ThumbSize = 160;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"];

    public static bool IsAllowed(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>Saves a photo for a person and returns the MediaRef to store (path relative to the data dir).</summary>
    public async Task<MediaRef> SaveAsync(string personId, Stream content, string originalFileName)
    {
        store.EnsureCanEdit();
        if (!IsAllowed(originalFileName))
            throw new InvalidOperationException($"Unsupported image type: {Path.GetExtension(originalFileName)}");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        ms.Position = 0;

        using var original = SKBitmap.Decode(ms)
            ?? throw new InvalidOperationException("The file is not a readable image.");

        var baseName = $"{personId}_{Guid.NewGuid():N}"[..(personId.Length + 9)];
        var mainFile = Path.Combine(store.PhotosDirectory, baseName + ".webp");
        var thumbFile = Path.Combine(store.PhotosDirectory, baseName + "_thumb.webp");

        // Main image: cap the longest edge.
        var scale = Math.Min(1.0, (double)MaxEdge / Math.Max(original.Width, original.Height));
        using var main = scale < 1.0
            ? original.Resize(new SKImageInfo((int)(original.Width * scale), (int)(original.Height * scale)),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : original.Copy();
        Encode(main, mainFile, 85);

        // Thumbnail: center-crop square, then scale down.
        var side = Math.Min(original.Width, original.Height);
        var cropRect = new SKRectI(
            (original.Width - side) / 2, (original.Height - side) / 2,
            (original.Width - side) / 2 + side, (original.Height - side) / 2 + side);
        using var square = new SKBitmap(side, side);
        original.ExtractSubset(square, cropRect);
        using var thumb = square.Resize(new SKImageInfo(ThumbSize, ThumbSize),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        Encode(thumb, thumbFile, 80);

        return new MediaRef { File = $"photos/{baseName}.webp" };
    }

    private static void Encode(SKBitmap bitmap, string path, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality)
            ?? throw new InvalidOperationException("Could not encode the image.");
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    /// <summary>Deletes a photo file and its thumbnail (ignores missing files).</summary>
    public void Delete(string relativeFile)
    {
        store.EnsureCanEdit();
        foreach (var file in new[] { relativeFile, PhotoPaths.ThumbPath(relativeFile) })
        {
            var full = Path.GetFullPath(Path.Combine(store.DataDirectory, file));
            // stay inside the data directory
            if (full.StartsWith(store.DataDirectory, StringComparison.Ordinal) && File.Exists(full))
                File.Delete(full);
        }
    }
}
