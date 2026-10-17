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
        var service = new LinkService(new FakeRepository(), new SequenceGenerator(), new TargetUrlValidator());

        var result = await service.ResolveAsync("UNKNOWN");

        Assert.Equal(ResolveLinkStatus.NotFound, result.Status);
        Assert.Null(result.TargetUrl);
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
        Assert.Equal(0, link.AccessCount);
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
        public List<Link> Saved { get; } = [];

        public Task<Link?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.SingleOrDefault(link => link.Code == code));

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
