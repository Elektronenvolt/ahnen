using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ahnen.Core.Helpers;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Ahnen.Desktop.Visualization;

/// <summary>Bounded, asynchronous thumbnail decoding; WPF itself need not have a WebP codec.</summary>
public sealed class TreePhotoCache(ILogger<TreePhotoCache> logger)
{
    public const int Capacity = 128;
    private readonly Dictionary<string, ImageSource?> _images = [];
    private readonly Queue<string> _order = [];
    private readonly HashSet<string> _pending = [];
    private readonly SemaphoreSlim _decoder = new(2);
    private readonly HashSet<string> _reported = [];
    public int Count => _images.Count;
    public event Action? ImageLoaded;

    public ImageSource? Get(string directory, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var key = Path.Combine(directory, relativePath);
        if (_images.TryGetValue(key, out var image)) return image;
        if (_pending.Count >= 24 || !_pending.Add(key)) return null;
        _ = LoadAsync(directory, relativePath, key);
        return null;
    }

    private async Task LoadAsync(string directory, string relativePath, string key)
    {
        ImageSource? image = null;
        try
        {
            await _decoder.WaitAsync();
            try
            {
                image = await Task.Run(() => Decode(directory, relativePath));
            }
            finally { _decoder.Release(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (_reported.Add(key)) logger.LogWarning(ex, "Tree photo {PhotoFile} could not be decoded.", relativePath);
        }
        finally
        {
            _pending.Remove(key);
            while (_images.Count >= Capacity) _images.Remove(_order.Dequeue());
            _images[key] = image;
            _order.Enqueue(key);
            ImageLoaded?.Invoke();
        }
    }

    private static ImageSource Decode(string directory, string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Photo paths must remain inside the document folder.");
        var thumbnail = Path.Combine(directory, PhotoPaths.ThumbPath(relativePath));
        if (File.Exists(thumbnail)) path = thumbnail;
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new IOException("The photo format could not be decoded.");
        var size = codec.GetScaledDimensions(Math.Min(1f, 160f / Math.Max(codec.Info.Width, codec.Info.Height)));
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success) throw new IOException($"Photo decoding failed: {result}.");
        var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null,
            bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
        source.Freeze();
        return source;
    }
}
