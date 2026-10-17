using System.Security.Cryptography;
using System.Text;
using LinkShortener.Api.Application;
using LinkShortener.Api.Domain;
using Xunit;

namespace LinkShortener.Tests.Application;

public sealed class LinkServiceTests
{
    [Fact]
    public async Task CreatePersistsGeneratedCodeAndOnlyTheHashOfManagementKey()
    {
        var repository = new FakeRepository();
        var service = new LinkService(repository, new SequenceGenerator("ABC2345"), new TargetUrlValidator());
        var before = DateTimeOffset.UtcNow;

        var result = await service.CreateAsync("https://8.8.8.8/path");

        Assert.Null(result.Error);
        Assert.Equal("ABC2345", result.Code);
        var link = Assert.Single(repository.Saved);
        Assert.Equal(result.Code, link.Code);
        Assert.Equal("https://8.8.8.8/path", link.TargetUrl);
        Assert.InRange(link.CreatedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Null(link.ExpiresAtUtc);
        Assert.Equal(0, link.AccessCount);
        Assert.Matches("^[A-F0-9]{64}$", result.ManagementKey!);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.ManagementKey!))),
            link.ManagementKeyHash);
        Assert.NotEqual(result.ManagementKey, link.ManagementKeyHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ftp://8.8.8.8/file")]
    [InlineData("http://127.0.0.1/")]
    public async Task InvalidUrlDoesNotGenerateCodeOrPersist(string? targetUrl)
    {
        var repository = new FakeRepository();
        var generator = new SequenceGenerator();
        var service = new LinkService(repository, generator, new TargetUrlValidator());

        var result = await service.CreateAsync(targetUrl);

        Assert.NotNull(result.Error);
        Assert.Null(result.Code);
        Assert.Null(result.ManagementKey);
        Assert.Empty(repository.Saved);
        Assert.Equal(0, generator.Calls);
        Assert.Equal(0, repository.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticCodeCollisionGeneratesAnotherCode(bool concurrentCollision)
    {
        var repository = new FakeRepository
        {
            ExistingCode = concurrentCollision ? null : "ABC2345",
            RejectFirstInsert = concurrentCollision
        };
        var generator = new SequenceGenerator("ABC2345", "XYZ6789");
        var service = new LinkService(repository, generator, new TargetUrlValidator());

        var result = await service.CreateAsync("http://8.8.8.8/");

        Assert.Null(result.Error);
        Assert.Equal("XYZ6789", result.Code);
        Assert.Equal(2, generator.Calls);
        Assert.Equal("XYZ6789", Assert.Single(repository.Saved).Code);
    }

    [Theory]
    [InlineData("ABCD")]
    [InlineData("ABCDEFGHIJKLMNOPQRST")]
    [InlineData("IO1L")]
    [InlineData("123456789")]
    public async Task CustomCodeIsPreservedWithoutGeneratingAnAutomaticCode(string code)
    {
        var repository = new FakeRepository();
        var generator = new SequenceGenerator();
        var service = new LinkService(repository, generator, new TargetUrlValidator());

        var result = await service.CreateAsync("https://8.8.8.8/", code);

        Assert.Null(result.Error);
        Assert.False(result.IsConflict);
        Assert.Equal(code, result.Code);
        Assert.Equal(code, Assert.Single(repository.Saved).Code);
        Assert.Equal(0, generator.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    [InlineData("AbCD")]
    [InlineData("ABC0")]
    [InlineData("AB C")]
    [InlineData(" ABCD")]
    [InlineData("AB-C")]
    [InlineData("ÁBCD")]
    [InlineData("ABCD\n")]
    public async Task InvalidCustomCodeDoesNotGenerateOrPersist(string code)
    {
        var repository = new FakeRepository();
        var generator = new SequenceGenerator();
        var service = new LinkService(repository, generator, new TargetUrlValidator());

        var result = await service.CreateAsync("https://8.8.8.8/", code);

        Assert.NotNull(result.Error);
        Assert.False(result.IsConflict);
        Assert.Null(result.Code);
        Assert.Null(result.ManagementKey);
        Assert.Empty(repository.Saved);
        Assert.Equal(0, repository.Checks);
        Assert.Equal(0, generator.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomCodeCollisionReturnsConflictWithoutRetry(bool concurrentCollision)
    {
        var repository = new FakeRepository
        {
            ExistingCode = concurrentCollision ? null : "CUSTOM1",
            RejectFirstInsert = concurrentCollision
        };
        var generator = new SequenceGenerator();
        var service = new LinkService(repository, generator, new TargetUrlValidator());

        var result = await service.CreateAsync("https://8.8.8.8/", "CUSTOM1");

        Assert.True(result.IsConflict);
        Assert.Equal("O código informado já está em uso.", result.Error);
        Assert.Null(result.Code);
        Assert.Null(result.ManagementKey);
        Assert.Empty(repository.Saved);
        Assert.Equal(1, repository.Checks);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task UnknownCodeDoesNotResolve()
    {
        var repository = new FakeRepository();
        var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator());

        var result = await service.ResolveAsync("UNKNOWN");

        Assert.Equal(ResolveLinkStatus.NotFound, result.Status);
        Assert.Null(result.TargetUrl);
        Assert.Empty(repository.IncrementedIds);
    }

    [Fact]
    public async Task EveryValidAccessIncrementsTheResolvedLinkAndForwardsCancellation()
    {
        var link = new Link
        {
            Code = "CUSTOM1", TargetUrl = "https://8.8.8.8/path", ManagementKeyHash = "hash"
        };
        var repository = new FakeRepository();
        repository.Saved.Add(link);
        var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator());
        using var cancellation = new CancellationTokenSource();

        for (var access = 0; access < 3; access++)
        {
            var result = await service.ResolveAsync(link.Code, cancellation.Token);

            Assert.Equal(ResolveLinkStatus.Found, result.Status);
            Assert.Equal(link.TargetUrl, result.TargetUrl);
        }

        Assert.Equal(new[] { link.Id, link.Id, link.Id }, repository.IncrementedIds);
        Assert.Equal(cancellation.Token, repository.IncrementCancellationToken);
    }

    [Theory]
    [InlineData(null, "2026-10-17T12:00:00Z", ResolveLinkStatus.Found)]
    [InlineData("2026-10-18T00:00:00Z", "2026-10-17T12:00:00Z", ResolveLinkStatus.Found)]
    [InlineData("2026-10-17T00:00:00Z", "2026-10-17T23:59:59.9999999Z", ResolveLinkStatus.Found)]
    [InlineData("2026-10-17T23:59:59Z", "2026-10-18T00:00:00Z", ResolveLinkStatus.Expired)]
    [InlineData("2026-10-17T23:00:00-03:00", "2026-10-18T23:59:59Z", ResolveLinkStatus.Found)]
    [InlineData("2026-10-17T23:00:00-03:00", "2026-10-19T00:00:00Z", ResolveLinkStatus.Expired)]
    public async Task ResolveHonorsTheEndOfTheExpirationDateInUtc(
        string? expiration, string now, ResolveLinkStatus expectedStatus)
    {
        var link = new Link
        {
            Code = "CUSTOM1", TargetUrl = "https://8.8.8.8/path", ManagementKeyHash = "hash",
            ExpiresAtUtc = expiration is null ? null : DateTimeOffset.Parse(expiration)
        };
        var repository = new FakeRepository();
        repository.Saved.Add(link);
        var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator(),
            new FixedTimeProvider(DateTimeOffset.Parse(now)));

        var result = await service.ResolveAsync(link.Code);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedStatus == ResolveLinkStatus.Found ? link.TargetUrl : null, result.TargetUrl);
        if (expectedStatus == ResolveLinkStatus.Found)
            Assert.Equal(link.Id, Assert.Single(repository.IncrementedIds));
        else
            Assert.Empty(repository.IncrementedIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("wrong-key")]
    [InlineData("CORRECT-KEY")]
    public async Task StatsRejectsMissingOrIncorrectKeyWithoutCountingAccess(string? key)
    {
        var repository = new FakeRepository();
        repository.Saved.Add(StatsLink());
        var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator());

        var result = await service.GetStatsAsync("STATS1", key);

        Assert.Equal(LinkStatsStatus.Unauthorized, result.Status);
        Assert.Null(result.Code);
        Assert.Null(result.AccessCount);
        Assert.Empty(repository.IncrementedIds);
    }

    [Fact]
    public async Task StatsWithCorrectKeyReadsRepositoryEveryTimeAndDoesNotCountAccess()
    {
        var repository = new FakeRepository();
        repository.Saved.Add(StatsLink());
        var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator());

        for (var request = 0; request < 2; request++)
        {
            var result = await service.GetStatsAsync("STATS1", "correct-key");
            Assert.Equal(LinkStatsStatus.Found, result.Status);
            Assert.Equal("STATS1", result.Code);
            Assert.Equal(0L, result.AccessCount);
        }

        Assert.Equal(2, repository.Reads);
        Assert.Empty(repository.IncrementedIds);
        Assert.Equal(LinkStatsStatus.NotFound, (await service.GetStatsAsync("UNKNOWN", "correct-key")).Status);
    }

    [Fact]
    public async Task WarmRedirectsAcrossServiceScopesSkipReadsButStillCountEveryAccess()
    {
        var repository = new FakeRepository();
        repository.Saved.Add(StatsLink());
        using var cache = new LinkRedirectCache();
        using var cancellation = new CancellationTokenSource();

        for (var request = 0; request < 5; request++)
        {
            var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator(),
                redirectCache: cache);
            var result = await service.ResolveAsync("STATS1", cancellation.Token);
            Assert.Equal(ResolveLinkStatus.Found, result.Status);
            Assert.Equal("https://8.8.8.8/", result.TargetUrl);
        }

        Assert.Equal(1, repository.Reads);
        Assert.Equal(5, repository.IncrementedIds.Count);
        Assert.All(repository.IncrementedIds, id => Assert.Equal(repository.Saved[0].Id, id));
        Assert.Equal(cancellation.Token, repository.IncrementCancellationToken);
    }

    [Fact]
    public async Task WarmRedirectStillChecksExpirationAtUtcMidnight()
    {
        var repository = new FakeRepository();
        repository.Saved.Add(new Link
        {
            Code = "EXPIRY1", TargetUrl = "https://8.8.8.8/", ManagementKeyHash = "hash",
            ExpiresAtUtc = DateTimeOffset.Parse("2026-10-17T00:00:00Z")
        });
        using var cache = new LinkRedirectCache();
        var clock = new MutableTimeProvider { Now = DateTimeOffset.Parse("2026-10-17T23:59:59Z") };
        var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator(), clock, cache);

        Assert.Equal(ResolveLinkStatus.Found, (await service.ResolveAsync("EXPIRY1")).Status);
        clock.Now = DateTimeOffset.Parse("2026-10-18T00:00:00Z");
        Assert.Equal(ResolveLinkStatus.Expired, (await service.ResolveAsync("EXPIRY1")).Status);
        Assert.Equal(1, repository.Reads);
        Assert.Single(repository.IncrementedIds);
    }

    [Fact]
    public async Task UnknownCodeIsNotCachedAndCanBeCreatedLater()
    {
        var repository = new FakeRepository();
        using var cache = new LinkRedirectCache();
        var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator(), redirectCache: cache);

        Assert.Equal(ResolveLinkStatus.NotFound, (await service.ResolveAsync("STATS1")).Status);
        repository.Saved.Add(StatsLink());
        Assert.Equal(ResolveLinkStatus.Found, (await service.ResolveAsync("STATS1")).Status);
        Assert.Equal(2, repository.Reads);
    }

    [Fact]
    public async Task CancelledWarmRedirectDoesNotCountAccess()
    {
        var repository = new FakeRepository();
        repository.Saved.Add(StatsLink());
        using var cache = new LinkRedirectCache();
        var service = new LinkService(repository, new SequenceGenerator(), new TargetUrlValidator(), redirectCache: cache);
        await service.ResolveAsync("STATS1");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveAsync("STATS1", cancellation.Token));
        Assert.Single(repository.IncrementedIds);
    }

    private static Link StatsLink() => new()
    {
        Code = "STATS1", TargetUrl = "https://8.8.8.8/",
        ManagementKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("correct-key")))
    };

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SequenceGenerator(params string[] codes) : ILinkCodeGenerator
    {
        public int Calls { get; private set; }
        public string Generate() => codes[Calls++];
    }

    private sealed class FakeRepository : ILinkRepository
    {
        public string? ExistingCode { get; init; }
        public bool RejectFirstInsert { get; set; }
        public int Checks { get; private set; }
        public int Reads { get; private set; }
        public List<Link> Saved { get; } = [];
        public List<Guid> IncrementedIds { get; } = [];
        public CancellationToken IncrementCancellationToken { get; private set; }

        public Task IncrementAccessCountAsync(Guid linkId, CancellationToken cancellationToken)
        {
            IncrementedIds.Add(linkId);
            IncrementCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<Link?> FindByCodeAsync(string code, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Saved.SingleOrDefault(link => link.Code == code));
        }

        public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
        {
            Checks++;
            return Task.FromResult(code == ExistingCode);
        }

        public Task<bool> TryAddAsync(Link link, CancellationToken cancellationToken)
        {
            if (RejectFirstInsert)
            {
                RejectFirstInsert = false;
                return Task.FromResult(false);
            }
            Saved.Add(link);
            return Task.FromResult(true);
        }
    }
}
