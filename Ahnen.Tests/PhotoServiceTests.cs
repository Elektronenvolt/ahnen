using Ahnen.Web.Services;
using SkiaSharp;

namespace Ahnen.Tests;

public class PhotoServiceTests : IDisposable
{
    private readonly TestStore _ts = new();

    public void Dispose() => _ts.Dispose();

    private static MemoryStream MakeImage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return new MemoryStream(data.ToArray());
    }

    [Fact]
    public async Task Save_creates_webp_and_thumbnail_and_delete_removes_them()
    {
        var store = _ts.Store;
        var person = store.AddPerson("Foto", "Test");
        var svc = new PhotoService(store);

        using var stream = MakeImage(2400, 1200);
        var media = await svc.SaveAsync(person.Id, stream, "pic.png");

        Assert.StartsWith("photos/", media.File);
        var main = Path.Combine(store.DataDirectory, media.File);
        var thumb = Path.Combine(store.DataDirectory, ChartDataService.ThumbPath(media.File));
        Assert.True(File.Exists(main));
        Assert.True(File.Exists(thumb));

        // main image was downscaled to max edge 1600
        using (var saved = SKBitmap.Decode(main))
        {
            Assert.True(Math.Max(saved.Width, saved.Height) <= 1600);
        }
        using (var savedThumb = SKBitmap.Decode(thumb))
        {
            Assert.Equal(160, savedThumb.Width);
            Assert.Equal(160, savedThumb.Height);
        }

        svc.Delete(media.File);
        Assert.False(File.Exists(main));
        Assert.False(File.Exists(thumb));
    }

    [Fact]
    public async Task Save_rejects_unsupported_extension()
    {
        var svc = new PhotoService(_ts.Store);
        using var stream = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync("I1", stream, "evil.exe"));
    }
}
