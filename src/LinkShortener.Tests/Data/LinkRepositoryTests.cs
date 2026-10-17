using LinkShortener.Api.Data;
using LinkShortener.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LinkShortener.Tests.Data;

public sealed class LinkRepositoryTests
{
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
