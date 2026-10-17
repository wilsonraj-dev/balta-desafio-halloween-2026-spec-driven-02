using LinkShortener.Api.Domain;
using Xunit;

namespace LinkShortener.Tests.Domain;

public sealed class LinkTests
{
    [Fact]
    public void NewLinkStartsWithoutAccessesOrExpirationAndWithUtcCreationTime()
    {
        var before = DateTimeOffset.UtcNow;

        var link = new Link
        {
            Code = "ABC1234",
            TargetUrl = "https://example.com/",
            ManagementKeyHash = "stored-hash"
        };

        Assert.NotEqual(Guid.Empty, link.Id);
        Assert.Equal(0, link.AccessCount);
        Assert.Null(link.ExpiresAtUtc);
        Assert.Equal(TimeSpan.Zero, link.CreatedAtUtc.Offset);
        Assert.InRange(link.CreatedAtUtc, before, DateTimeOffset.UtcNow);
    }
}
