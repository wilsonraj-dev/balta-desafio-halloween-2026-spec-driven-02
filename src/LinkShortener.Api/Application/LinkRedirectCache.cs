using Microsoft.Extensions.Caching.Memory;

namespace LinkShortener.Api.Application;

public sealed record CachedRedirect(Guid LinkId, string TargetUrl, DateTimeOffset? ExpiresAtUtc);

public sealed class LinkRedirectCache : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 1024 });

    public CachedRedirect? Find(string code) => cache.Get<CachedRedirect>(code);

    public void Store(string code, CachedRedirect redirect) => cache.Set(code, redirect,
        new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(5),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });

    public void Dispose() => cache.Dispose();
}
