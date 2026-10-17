using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using LinkShortener.Api.Data;
using LinkShortener.Api.Endpoints;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LinkShortener.Tests.Endpoints;

public sealed class LinkEndpointsTests
{
    [Fact]
    public async Task PostCreatesLinkReturns201AndPersistsAfterReopeningDatabase()
    {
        await WithApiAsync(async (client, databasePath) =>
        {
            const string targetUrl = "https://8.8.8.8/path?value=1";
            var response = await client.PostAsJsonAsync("/api/links", new { targetUrl });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
            Assert.NotNull(body);
            Assert.Matches("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{7}$", body.Code);
            Assert.Equal(new Uri(client.BaseAddress!, body.Code).AbsoluteUri, body.ShortUrl);
            Assert.Equal(body.ShortUrl, response.Headers.Location!.AbsoluteUri);

            await using var context = CreateContext(databasePath);
            var link = await context.Links.AsNoTracking().SingleAsync();
            Assert.Equal(body.Code, link.Code);
            Assert.Equal(targetUrl, link.TargetUrl);
            Assert.Equal(0, link.AccessCount);
            Assert.Null(link.ExpiresAtUtc);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.ManagementKey))),
                link.ManagementKeyHash);

            var secondResponse = await client.PostAsJsonAsync("/api/links", new { targetUrl });
            Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
            var secondBody = await secondResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();
            Assert.NotNull(secondBody);
            Assert.NotEqual(body.Code, secondBody.Code);
            Assert.NotEqual(body.ManagementKey, secondBody.ManagementKey);
            Assert.Equal(2, await context.Links.CountAsync());
        });
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"targetUrl\":\"ftp://8.8.8.8/\"}")]
    [InlineData("{\"targetUrl\":\"http://127.0.0.1/\"}")]
    [InlineData("{invalid")]
    [InlineData("{\"targetUrl\":123}")]
    public async Task InvalidInputReturns400WithProblemDetailsAndDoesNotPersist(string json)
    {
        await WithApiAsync(async (client, databasePath) =>
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/links", content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(400, problem.Status);
            await using var context = CreateContext(databasePath);
            Assert.Empty(await context.Links.ToListAsync());
        });
    }

    private static LinkDbContext CreateContext(string path) => new(
        new DbContextOptionsBuilder<LinkDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);

    private static async Task WithApiAsync(Func<HttpClient, string, Task> test)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"links-api-{Guid.NewGuid():N}.db");
        try
        {
            await using var app = Program.CreateApp([
                "--urls", "http://127.0.0.1:0",
                "--environment", "Production",
                "--ConnectionStrings:Links", $"Data Source={databasePath};Pooling=False"
            ]);
            await using (var scope = app.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<LinkDbContext>().Database.MigrateAsync();

            await app.StartAsync();
            try
            {
                var server = app.Services.GetRequiredService<IServer>();
                var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
                using var client = new HttpClient { BaseAddress = new Uri(address) };
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
