using System.Security.Cryptography;
using System.Text;
using LinkShortener.Api.Domain;

namespace LinkShortener.Api.Application;

public sealed record CreateLinkResult(
    string? Code, string? ManagementKey, string? Error, bool IsConflict = false);

public enum ResolveLinkStatus { Found, NotFound, Expired }
public sealed record ResolveLinkResult(ResolveLinkStatus Status, string? TargetUrl = null);

public sealed class LinkService(
    ILinkRepository repository,
    ILinkCodeGenerator codeGenerator,
    TargetUrlValidator targetUrlValidator,
    TimeProvider? timeProvider = null)
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
        var link = await repository.FindByCodeAsync(code, cancellationToken);
        if (link is null)
            return new(ResolveLinkStatus.NotFound);

        // RF-06: the link remains valid through the end of its expiration date in UTC.
        if (link.ExpiresAtUtc is { } expiresAt
            && expiresAt.UtcDateTime.Date < (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime.Date)
            return new(ResolveLinkStatus.Expired);

        return new(ResolveLinkStatus.Found, link.TargetUrl);
    }

    private static CreateLinkResult CodeConflict() =>
        new(null, null, "O código informado já está em uso.", IsConflict: true);
}
