using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LinkShortener.Tests.Endpoints;

public sealed class FrontendEndpointsTests
{
    [Fact]
    public async Task GetRootReturnsBlazorHomePageWithoutDatabaseSetup()
    {
        await using var app = Program.CreateApp([
            "--urls", "http://127.0.0.1:0",
            "--environment", "Production",
            "--ConnectionStrings:Links", "Data Source=:memory:"
        ]);
        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("<html lang=\"pt-BR\">", html);
            Assert.Contains("<title>", html);
            Assert.Contains("<main", html);
            Assert.Contains("status-title", html);
            Assert.Contains("Feito com Blazor", html);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
