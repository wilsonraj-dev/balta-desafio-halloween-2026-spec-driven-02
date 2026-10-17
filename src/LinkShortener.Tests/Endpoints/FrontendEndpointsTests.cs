using System.Net;
using System.Text.RegularExpressions;
using LinkShortener.Api.Data;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LinkShortener.Tests.Endpoints;

public sealed class FrontendEndpointsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("SCREEN1")]
    public async Task SubmitHomeFormCreatesPersistentLinkAndDisplaysWorkingShortUrl(string code)
    {
        await WithFrontendAsync(async (client, databasePath) =>
        {
            using var response = await SubmitFormAsync(client, "https://8.8.8.8/path?value=1", code);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            await using var context = CreateContext(databasePath);
            var link = await context.Links.AsNoTracking().SingleAsync();
            Assert.Equal("https://8.8.8.8/path?value=1", link.TargetUrl);
            if (code.Length > 0)
                Assert.Equal(code, link.Code);
            var shortUrl = new Uri(client.BaseAddress!, link.Code).AbsoluteUri;
            Assert.Contains($"href=\"{shortUrl}\"", html);
            Assert.Contains("Link criado!", html);
            Assert.Contains("Chave de gerenciamento:", html);
            var key = Regex.Match(html, "<code>([A-F0-9]{64})</code>").Groups[1].Value;
            Assert.NotEmpty(key);
            client.DefaultRequestHeaders.Add("X-Management-Key", key);
            using var stats = await client.GetAsync($"/api/links/{link.Code}/stats");
            Assert.Equal(HttpStatusCode.OK, stats.StatusCode);

            using var redirect = await client.GetAsync(shortUrl);
            Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
            Assert.Equal(link.TargetUrl, redirect.Headers.Location!.OriginalString);
        });
    }

    [Fact]
    public async Task SubmitHomeFormShowsValidationAndDuplicateCodeErrors()
    {
        await WithFrontendAsync(async (client, databasePath) =>
        {
            using var invalid = await SubmitFormAsync(client, "ftp://8.8.8.8/", "");
            var invalidHtml = WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
            Assert.Contains("role=\"alert\"", invalidHtml);
            Assert.Contains("protocolo http ou https", invalidHtml);
            await using var context = CreateContext(databasePath);
            Assert.Empty(await context.Links.ToListAsync());

            using var created = await SubmitFormAsync(client, "https://8.8.8.8/", "SCREEN1");
            created.EnsureSuccessStatusCode();
            using var duplicate = await SubmitFormAsync(client, "https://8.8.8.8/other", "SCREEN1");
            var duplicateHtml = WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync());
            Assert.Contains("O código informado já está em uso.", duplicateHtml);
            Assert.DoesNotContain("Link criado!", duplicateHtml);
            Assert.Single(await context.Links.ToListAsync());
        });
    }

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

    private static async Task<HttpResponseMessage> SubmitFormAsync(HttpClient client, string targetUrl, string code)
    {
        var html = await client.GetStringAsync("/");
        var fields = new Dictionary<string, string>();
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>"))
        {
            if (!input.Value.Contains("type=\"hidden\""))
                continue;
            var name = Regex.Match(input.Value, "name=\"([^\"]+)\"").Groups[1].Value;
            var value = Regex.Match(input.Value, "value=\"([^\"]*)\"").Groups[1].Value;
            fields.Add(WebUtility.HtmlDecode(name), WebUtility.HtmlDecode(value));
        }
        Assert.Contains("__RequestVerificationToken", fields.Keys);
        fields["Input.TargetUrl"] = targetUrl;
        fields["Input.Code"] = code;
        using var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync("/", content);
    }

    private static LinkDbContext CreateContext(string path) => new(
        new DbContextOptionsBuilder<LinkDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);

    private static async Task WithFrontendAsync(Func<HttpClient, string, Task> test)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"links-frontend-{Guid.NewGuid():N}.db");
        try
        {
            await using var app = Program.CreateApp([
                "--urls", "http://127.0.0.1:0", "--environment", "Production",
                "--ConnectionStrings:Links", $"Data Source={databasePath};Pooling=False"
            ]);
            await using (var scope = app.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<LinkDbContext>().Database.MigrateAsync();
            await app.StartAsync();
            try
            {
                var server = app.Services.GetRequiredService<IServer>();
                var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
                await test(client, databasePath);
            }
            finally
            {
                await app.StopAsync();
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                File.Delete(databasePath + suffix);
        }
    }
}
