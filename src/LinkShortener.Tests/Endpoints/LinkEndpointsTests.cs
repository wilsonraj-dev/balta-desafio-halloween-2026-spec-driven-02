using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using LinkShortener.Api.Data;
using LinkShortener.Api.Endpoints;
using LinkShortener.Api.Domain;
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

    [Theory]
    [InlineData("ABCD")]
    [InlineData("ABCDEFGHIJKLMNOPQRST")]
    [InlineData("IO1L")]
    public async Task PostWithCustomCodeReturns201AndDuplicateReturns409(string code)
    {
        await WithApiAsync(async (client, databasePath) =>
        {
            var response = await client.PostAsJsonAsync("/api/links",
                new { targetUrl = "https://8.8.8.8/original", code });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
            Assert.NotNull(body);
            Assert.Equal(code, body.Code);
            Assert.Equal(new Uri(client.BaseAddress!, code).AbsoluteUri, body.ShortUrl);
            Assert.Equal(body.ShortUrl, response.Headers.Location!.AbsoluteUri);

            var duplicate = await client.PostAsJsonAsync("/api/links",
                new { targetUrl = "https://8.8.8.8/duplicate", code });

            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType!.MediaType);
            var problem = await duplicate.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(409, problem.Status);
            Assert.Equal("O código informado já está em uso.", problem.Detail);
            await using var context = CreateContext(databasePath);
            var link = Assert.Single(await context.Links.ToListAsync());
            Assert.Equal(code, link.Code);
            Assert.Equal("https://8.8.8.8/original", link.TargetUrl);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    [InlineData("abcd")]
    [InlineData("ABC0")]
    [InlineData("AB C")]
    [InlineData("AB-C")]
    public async Task InvalidCustomCodeReturns400WithProblemDetails(string code)
    {
        await WithApiAsync(async (client, databasePath) =>
        {
            var response = await client.PostAsJsonAsync("/api/links",
                new { targetUrl = "https://8.8.8.8/", code });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(400, problem.Status);
            await using var context = CreateContext(databasePath);
            Assert.Empty(await context.Links.ToListAsync());
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CUSTOM1")]
    public async Task GetCreatedLinkReturns302WithExactTargetUrl(string? code)
    {
        await WithApiAsync(async (client, _) =>
        {
            const string targetUrl = "https://8.8.8.8/path?value=1&other=2#fragment";
            var creation = await client.PostAsJsonAsync("/api/links", new { targetUrl, code });
            Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
            var body = await creation.Content.ReadFromJsonAsync<CreateLinkResponse>();
            Assert.NotNull(body);

            var response = await client.GetAsync("/" + body.Code);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal(targetUrl, response.Headers.Location!.OriginalString);
        });
    }

    [Fact]
    public async Task GetUnknownCodeReturns404()
    {
        await WithApiAsync(async (client, _) =>
        {
            var response = await client.GetAsync("/UNKNOWN");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(404, problem.Status);
            Assert.Null(response.Headers.Location);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPersistedLinkHonorsExpirationDate(bool expired)
    {
        await WithApiAsync(async (client, databasePath) =>
        {
            var utcToday = DateTimeOffset.UtcNow.UtcDateTime.Date;
            await using (var context = CreateContext(databasePath))
            {
                context.Links.Add(new Link
                {
                    Code = "EXPIRY1", TargetUrl = "https://8.8.8.8/", ManagementKeyHash = "hash",
                    ExpiresAtUtc = new DateTimeOffset(utcToday.AddDays(expired ? -1 : 1), TimeSpan.Zero)
                });
                await context.SaveChangesAsync();
            }

            var response = await client.GetAsync("/EXPIRY1");

            Assert.Equal(expired ? HttpStatusCode.Gone : HttpStatusCode.Found, response.StatusCode);
            if (expired)
            {
                Assert.Null(response.Headers.Location);
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                Assert.NotNull(problem);
                Assert.Equal(410, problem.Status);
                await using var context = CreateContext(databasePath);
                Assert.Equal(0, (await context.Links.SingleAsync()).AccessCount);
            }
            else
                Assert.Equal("https://8.8.8.8/", response.Headers.Location!.AbsoluteUri);
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
