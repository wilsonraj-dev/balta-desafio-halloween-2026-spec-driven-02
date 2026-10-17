using System.Diagnostics;
using LinkShortener.Api.Application;
using LinkShortener.Api.Data;
using LinkShortener.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace LinkShortener.Tests.Application;

public sealed class RedirectCachePerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CompareWarmRedirectLatencyWithSqliteAndPreserveEveryIncrement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new LinkDbContext(new DbContextOptionsBuilder<LinkDbContext>()
            .UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Links.Add(new Link
        {
            Code = "CACHE1", TargetUrl = "https://8.8.8.8/", ManagementKeyHash = "hash"
        });
        await context.SaveChangesAsync();
        var repository = new LinkRepository(context);
        using var cache = new LinkRedirectCache();
        var uncached = new LinkService(repository, new LinkCodeGenerator(), new TargetUrlValidator());
        var cached = new LinkService(repository, new LinkCodeGenerator(), new TargetUrlValidator(), redirectCache: cache);

        // Warm up EF queries and JIT before comparing; alternate order to limit ordering bias.
        await MeasureAsync(uncached, 20);
        await MeasureAsync(cached, 20);
        const int iterations = 200;
        var uncachedSamples = new List<double>();
        var cachedSamples = new List<double>();
        for (var round = 0; round < 5; round++)
        {
            if (round % 2 == 0)
            {
                uncachedSamples.Add(await MeasureAsync(uncached, iterations));
                cachedSamples.Add(await MeasureAsync(cached, iterations));
            }
            else
            {
                cachedSamples.Add(await MeasureAsync(cached, iterations));
                uncachedSamples.Add(await MeasureAsync(uncached, iterations));
            }
        }

        var baseline = uncachedSamples.Order().ElementAt(2);
        var warm = cachedSamples.Order().ElementAt(2);
        output.WriteLine($"SQLite in-memory; median of 5 batches of {iterations} redirects (service only).");
        output.WriteLine($"Without cache: {baseline:F2} us/redirect; warm cache: {warm:F2} us/redirect.");
        output.WriteLine($"Latency reduction: {(1 - warm / baseline) * 100:F1}%.");
        // Timing is diagnostic, not a flaky CI threshold. Database state proves writes are retained.
        Assert.Equal(2040, (await context.Links.AsNoTracking().SingleAsync()).AccessCount);
    }

    private static async Task<double> MeasureAsync(LinkService service, int iterations)
    {
        var start = Stopwatch.GetTimestamp();
        for (var request = 0; request < iterations; request++)
        {
            var result = await service.ResolveAsync("CACHE1");
            Assert.Equal(ResolveLinkStatus.Found, result.Status);
        }
        return Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
    }
}
