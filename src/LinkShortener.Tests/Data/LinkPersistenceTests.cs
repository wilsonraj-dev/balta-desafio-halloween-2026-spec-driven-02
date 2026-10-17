using LinkShortener.Api.Data;
using LinkShortener.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LinkShortener.Tests.Data;

public sealed class LinkPersistenceTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"links-{Guid.NewGuid():N}.db");

    private LinkDbContext CreateContext() => new(new DbContextOptionsBuilder<LinkDbContext>()
        .UseSqlite($"Data Source={databasePath};Pooling=False").Options);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationPersistsAllLinkFieldsAfterReopeningDatabase(bool hasExpiration)
    {
        var link = new Link
        {
            Code = "ABC1234",
            TargetUrl = "https://example.com/path?value=1",
            CreatedAtUtc = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
            ExpiresAtUtc = hasExpiration
                ? new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero) : null,
            ManagementKeyHash = "stored-hash"
        };

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Links.Add(link);
            await context.SaveChangesAsync();
            // Verifica que o mapeamento também preserva contagens existentes não nulas.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Links SET AccessCount = {42L} WHERE Id = {link.Id}");
        }

        await using var reopened = CreateContext();
        Assert.Single(await reopened.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await reopened.Database.GetPendingMigrationsAsync());
        var persisted = await reopened.Links.AsNoTracking().SingleAsync();
        Assert.Equal(link.Id, persisted.Id);
        Assert.Equal(link.Code, persisted.Code);
        Assert.Equal(link.TargetUrl, persisted.TargetUrl);
        Assert.Equal(link.CreatedAtUtc, persisted.CreatedAtUtc);
        Assert.Equal(link.ExpiresAtUtc, persisted.ExpiresAtUtc);
        Assert.Equal(42, persisted.AccessCount);
        Assert.Equal(link.ManagementKeyHash, persisted.ManagementKeyHash);
    }

    [Fact]
    public async Task UniqueIndexRejectsDuplicateCode()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        context.Links.Add(new Link
        {
            Code = "ABC1234", TargetUrl = "https://example.com/", ManagementKeyHash = "hash-one"
        });
        await context.SaveChangesAsync();
        context.Links.Add(new Link
        {
            Code = "ABC1234", TargetUrl = "https://example.org/", ManagementKeyHash = "hash-two"
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Equal(2067, sqliteException.SqliteExtendedErrorCode);
        Assert.Equal(1, await context.Links.CountAsync());
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            File.Delete(databasePath + suffix);
    }
}
