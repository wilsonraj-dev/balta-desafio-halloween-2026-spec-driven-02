using System.Security.Cryptography;
using System.Text;
using LinkShortener.Api.Domain;

namespace LinkShortener.Api.Application;

public sealed record CreateLinkResult(
    string? Code, string? ManagementKey, string? Error, bool IsConflict = false);

public enum ResolveLinkStatus { Found, NotFound, Expired }
public sealed record ResolveLinkResult(ResolveLinkStatus Status, string? TargetUrl = null);

public enum LinkStatsStatus { Found, NotFound, Unauthorized }
public sealed record LinkStatsResult(LinkStatsStatus Status, string? Code = null, long? AccessCount = null);

public sealed class LinkService(
    ILinkRepository repository,
    ILinkCodeGenerator codeGenerator,
    TargetUrlValidator targetUrlValidator,
    TimeProvider? timeProvider = null,
    LinkRedirectCache? redirectCache = null)
{
    public Task<CreateLinkResult> CreateAsync(
        string? targetUrl, CancellationToken cancellationToken = default) =>
        CreateAsync(targetUrl, null, cancellationToken);

    public async Task<CreateLinkResult> CreateAsync(
        string? targetUrl, string? customCode, CancellationToken cancellationToken = default)
    {
        if (customCode is not null && (customCode.Length is < 4 or > 20
            || customCode.Any(character => character is not (>= 'A' and <= 'Z')
                and not (>= '1' and <= '9'))))
            return new(null, null,
                "O código próprio deve ter de 4 a 20 caracteres, usando apenas letras maiúsculas de A a Z e números de 1 a 9.");

        var error = await targetUrlValidator.ValidateAsync(targetUrl, cancellationToken);
        if (error is not null)
            return new(null, null, error);

        var managementKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var managementKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(managementKey)));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var code = customCode ?? codeGenerator.Generate();
            if (await repository.CodeExistsAsync(code, cancellationToken))
            {
                if (customCode is not null)
                    return CodeConflict();

                continue;
            }

            var link = new Link
            {
                Code = code,
                TargetUrl = targetUrl!,
                ManagementKeyHash = managementKeyHash
            };

            // A concurrent insert may claim the code after the existence check.
            if (await repository.TryAddAsync(link, cancellationToken))
                return new(code, managementKey, null);
            if (customCode is not null)
                return CodeConflict();
        }
    }

    public async Task<ResolveLinkResult> ResolveAsync(
        string code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var redirect = redirectCache?.Find(code);
        var cacheMiss = redirect is null;
        if (redirect is null)
        {
            var link = await repository.FindByCodeAsync(code, cancellationToken);
            if (link is null)
                return new(ResolveLinkStatus.NotFound);

            redirect = new CachedRedirect(link.Id, link.TargetUrl, link.ExpiresAtUtc);
        }

        // RF-06: the link remains valid through the end of its expiration date in UTC.
        if (redirect.ExpiresAtUtc is { } expiresAt
            && expiresAt.UtcDateTime.Date < (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime.Date)
            return new(ResolveLinkStatus.Expired);

        await repository.IncrementAccessCountAsync(redirect.LinkId, cancellationToken);
        if (cacheMiss)
            redirectCache?.Store(code, redirect);
        return new(ResolveLinkStatus.Found, redirect.TargetUrl);
    }

    public async Task<LinkStatsResult> GetStatsAsync(
        string code, string? managementKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(managementKey))
            return new(LinkStatsStatus.Unauthorized);

        var link = await repository.FindByCodeAsync(code, cancellationToken);
        if (link is null)
            return new(LinkStatsStatus.NotFound);

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(managementKey));
        var storedHash = Convert.FromHexString(link.ManagementKeyHash);
        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, storedHash))
            return new(LinkStatsStatus.Unauthorized);

        return new(LinkStatsStatus.Found, link.Code, link.AccessCount);
    }

    private static CreateLinkResult CodeConflict() =>
        new(null, null, "O código informado já está em uso.", IsConflict: true);
}
