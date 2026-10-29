using Ahnen.Core.Services;
using Ahnen.Web.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ahnen.Tests;

public class ApplicationLogViewerTests
{
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public async Task Viewer_colors_only_level_labels_and_renders_buffered_logs_as_escaped_text(LogLevel level)
    {
        var buffer = new ApplicationLogBuffer();
        buffer.Append(level, "Startup", 1, "<script>alert('log')</script>",
            new InvalidOperationException("<b>exception</b>"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddSingleton(buffer);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ApplicationLogs>(ParameterView.Empty);
            return component.ToHtmlString();
        });

        Assert.Contains("aria-expanded=\"false\"", html);
        Assert.Contains("hidden", html);
        Assert.Contains("<div class=\"log-entry\">", html);
        Assert.Contains($"<strong class=\"log-level-{level.ToString().ToLowerInvariant()}\">{level}</strong>", html);
        Assert.Equal(1, html.Split("log-level-").Length - 1);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;exception&lt;/b&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Logs", html);
    }
}
