using System.Net;
using LinkShortener.Api.Components.Pages;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LinkShortener.Tests.Components;

public sealed class HomeTests
{
    [Fact]
    public async Task HomeRendersWelcomeAndExplainsShorteningScreenIsComingSoon()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<Home>();
            return component.ToHtmlString();
        });

        Assert.Contains("<main", html);
        var content = WebUtility.HtmlDecode(html);
        Assert.Contains("Menos caracteres, mais conexões.", content);
        Assert.Contains("A tela para encurtar links será disponibilizada em breve.", content);
    }
}
