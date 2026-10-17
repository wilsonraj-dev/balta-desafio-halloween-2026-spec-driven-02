using LinkShortener.Api.Data;
using LinkShortener.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LinkShortener.Tests.Data;

public sealed class LinkRepositoryTests
{
    [Fact]
    public async Task ConcurrentIncrementsPreserveExistingCountAndOnlyUpdateTheRequestedLink()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"links-counter-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LinkDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        var target = CreateLink("TARGET1", "https://8.8.8.8/target");
        var other = CreateLink("OTHER12", "https://8.8.8.8/other");
        const int workers = 24;
        const int accessesPerWorker = 3;
        const int initialCount = 5;
        try
        {
            await using (var context = new LinkDbContext(options))
            {
                await context.Database.MigrateAsync();
                context.Links.AddRange(target, other);
                await context.SaveChangesAsync();
                var repository = new LinkRepository(context);
                for (var access = 0; access < initialCount; access++)
                    await repository.IncrementAccessCountAsync(target.Id, default);
            }

            var ready = 0;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
            {
                await using var context = new LinkDbContext(options);
                var repository = new LinkRepository(context);
                // All workers read the same count before any concurrent increment starts.
                var snapshot = await repository.FindByCodeAsync(target.Code, default);
                if (Interlocked.Increment(ref ready) == workers)
                    start.SetResult();
                await start.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.NotNull(snapshot);
                Assert.Equal(initialCount, snapshot.AccessCount);

                for (var access = 0; access < accessesPerWorker; access++)
                    await repository.IncrementAccessCountAsync(target.Id, default);
            })).ToArray();

            await Task.WhenAll(tasks);

            await using var verification = new LinkDbContext(options);
            Assert.Equal(initialCount + workers * accessesPerWorker,
                (await verification.Links.SingleAsync(link => link.Id == target.Id)).AccessCount);
            Assert.Equal(0, (await verification.Links.SingleAsync(link => link.Id == other.Id)).AccessCount);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                File.Delete(databasePath + suffix);
        }
    }

    [Fact]
    public async Task DuplicateCodeAllowsRetryWithoutLosingTheExistingLink()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new LinkDbContext(new DbContextOptionsBuilder<LinkDbContext>()
            .UseSqlite(connection).Options);
        await context.Database.MigrateAsync();
        var repository = new LinkRepository(context);
        var original = CreateLink("ABC2345", "https://8.8.8.8/original");

        Assert.True(await repository.TryAddAsync(original, default));
        Assert.True(await repository.CodeExistsAsync(original.Code, default));
        Assert.False(await repository.TryAddAsync(CreateLink("ABC2345", "https://8.8.8.8/duplicate"), default));
        Assert.True(await repository.TryAddAsync(CreateLink("XYZ6789", "https://8.8.8.8/retry"), default));

        context.ChangeTracker.Clear();
        Assert.Equal(2, await context.Links.CountAsync());
        Assert.Equal(original.TargetUrl,
            (await context.Links.SingleAsync(link => link.Code == original.Code)).TargetUrl);
    }

    private static Link CreateLink(string code, string targetUrl) => new()
    {
        Code = code, TargetUrl = targetUrl, ManagementKeyHash = "hash"
    };
}
