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
