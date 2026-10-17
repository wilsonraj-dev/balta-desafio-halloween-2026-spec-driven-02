namespace LinkShortener.Api.Domain;

public sealed class Link
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Code { get; init; }
    public required string TargetUrl { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public long AccessCount { get; private set; }
    public required string ManagementKeyHash { get; init; }
}
