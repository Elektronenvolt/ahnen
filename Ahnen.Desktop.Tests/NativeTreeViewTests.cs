using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ahnen.Desktop.Services;
using Ahnen.Desktop.ViewModels;
using Ahnen.Desktop.Views;
using Ahnen.Desktop.Visualization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit.Abstractions;

namespace Ahnen.Desktop.Tests;

[Collection("Shared localizer")]
public class NativeTreeViewTests(ITestOutputHelper output)
{
    [Fact]
    public Task Actual_window_displays_the_supplied_tree_and_supports_native_navigation_and_photos() => RunOnDispatcher(async () =>
    {
        using var root = new TempRoot();
        File.Copy(Path.Combine(AppContext.BaseDirectory, DesktopPaths.DocumentFileName),
            Path.Combine(root.Paths.DataDirectory, DesktopPaths.DocumentFileName));
        using var services = App.BuildServices(root.Paths);
        var application = new App();
        application.InitializeComponent();
        var model = services.GetRequiredService<TreePageViewModel>();
        var main = services.GetRequiredService<MainViewModel>();
        var bindingErrors = new StringBuilder();
        using var writer = new StringWriter(bindingErrors);
        using var listener = new TextWriterTraceListener(writer);
        var bindingTrace = PresentationTraceSources.DataBindingSource;
        var oldLevel = bindingTrace.Switch.Level;
        bindingTrace.Switch.Level = SourceLevels.Error;
        bindingTrace.Listeners.Add(listener);
        var window = new MainWindow { DataContext = main, Width = 1280, Height = 800, ShowActivated = false };
        try
        {
            window.Show();
            await model.InitializeAsync();
            await model.SelectionTask;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var canvas = Descendant<TreeCanvas>(window);

            Assert.Equal(200, model.People.Count);
            Assert.NotEmpty(model.Scene.Cards);
            Assert.Same(model.Scene, canvas.Scene);
            Assert.True(canvas.ActualWidth > 100 && canvas.ActualHeight > 100);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var cardPixels = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (pixels[i] == 255 && pixels[i + 1] == 235 && pixels[i + 2] == 220) cardPixels++;
            Assert.True(cardPixels > 10, "The actual window must render person cards, not only a status count.");

            model.ShowAllCommand.Execute(null);
            await model.LayoutTask;
            Assert.Equal(200, canvas.Scene!.Cards.Count);
            canvas.Fit();
            var fitted = canvas.ZoomLevel;
            model.ZoomInCommand.Execute(null);
            Assert.True(canvas.ZoomLevel > fitted);
            model.ZoomOutCommand.Execute(null);
            Assert.Equal(fitted, canvas.ZoomLevel, 6);
            var target = model.People[^1];
            model.SelectPerson(target.Id, true);
            await model.LayoutTask;
            Assert.Equal(target.Id, canvas.FocusId);
            Assert.True(canvas.ZoomLevel >= 0.85);
            model.Chart = TreeChart.Ancestors;
            await model.LayoutTask;
            main.SetLanguageCommand.Execute("de");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(TreeChart.Ancestors, model.Chart);
            Assert.Equal(TreeOrientation.Vertical, model.Orientation);
            Assert.Equal(TreeColors.Sex, model.Colors);
            main.SetLanguageCommand.Execute("en");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            main.SelectPageCommand.Execute("1");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            main.SelectPageCommand.Execute("0");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.NotEmpty(Descendant<TreeCanvas>(window).Scene!.Cards);
            bindingTrace.Flush();
            Assert.True(bindingErrors.Length == 0, bindingErrors.ToString());

            await VerifyPhotosAsync(root.Paths.DataDirectory);
            var largeScene = TreeLayout.Build(TreeLayoutTests.Synthetic(10000), new("P0", Overview: true));
            var largeCanvas = new TreeCanvas { Scene = largeScene };
            largeCanvas.Measure(new Size(1000, 600));
            largeCanvas.Arrange(new Rect(0, 0, 1000, 600));
            largeCanvas.Fit();
            var watch = Stopwatch.StartNew();
            var overviewBitmap = new RenderTargetBitmap(1000, 600, 96, 96, PixelFormats.Pbgra32);
            overviewBitmap.Render(largeCanvas);
            output.WriteLine($"10,000-person native overview: first bitmap render {watch.Elapsed.TotalMilliseconds:F1} ms, 19,998 links.");
            largeCanvas.CenterOn("P9999");
            watch.Restart();
            overviewBitmap.Render(largeCanvas);
            output.WriteLine($"10,000-person native focused viewport: bitmap render {watch.Elapsed.TotalMilliseconds:F1} ms.");
            Assert.Equal(0, VisualTreeHelper.GetChildrenCount(largeCanvas));
            Assert.Equal(10000, largeCanvas.Scene!.Cards.Count);
            Assert.True(largeCanvas.ZoomLevel >= 0.85);
        }
        finally
        {
            bindingTrace.Listeners.Remove(listener);
            bindingTrace.Switch.Level = oldLevel;
            window.Close();
            application.Shutdown();
        }
    });

    private static async Task VerifyPhotosAsync(string directory)
    {
        var cache = new TreePhotoCache(NullLogger<TreePhotoCache>.Instance);
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Red);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
        Directory.CreateDirectory(Path.Combine(directory, "photos"));
        using (var stream = File.Create(Path.Combine(directory, "photos", "test.webp"))) encoded.SaveTo(stream);
        for (var i = 0; i <= TreePhotoCache.Capacity; i++)
        {
            var relative = Path.Combine("photos", i + ".webp");
            File.Copy(Path.Combine(directory, "photos", "test.webp"), Path.Combine(directory, relative));
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLoaded() => loaded.TrySetResult();
            cache.ImageLoaded += OnLoaded;
            try
            {
                Assert.Null(cache.Get(directory + Path.DirectorySeparatorChar, relative));
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var image = cache.Get(directory + Path.DirectorySeparatorChar, relative);
                Assert.NotNull(image);
                Assert.True(image.IsFrozen);
            }
            finally { cache.ImageLoaded -= OnLoaded; }
        }
        Assert.Equal(TreePhotoCache.Capacity, cache.Count);
    }

    private static T Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>([root]);
        while (queue.TryDequeue(out var current))
        {
            if (current is T found) return found;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++) queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }
        throw new InvalidOperationException($"No {typeof(T).Name} was displayed in the actual window.");
    }

    private static Task RunOnDispatcher(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
