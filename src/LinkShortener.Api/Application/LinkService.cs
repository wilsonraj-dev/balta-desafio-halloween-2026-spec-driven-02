using System.Security.Cryptography;
using System.Text;
using LinkShortener.Api.Domain;

namespace LinkShortener.Api.Application;

public sealed record CreateLinkResult(string? Code, string? ManagementKey, string? Error);

public sealed class LinkService(
    ILinkRepository repository,
    ILinkCodeGenerator codeGenerator,
    TargetUrlValidator targetUrlValidator)
{
    public async Task<CreateLinkResult> CreateAsync(
        string? targetUrl, CancellationToken cancellationToken = default)
    {
        var error = await targetUrlValidator.ValidateAsync(targetUrl, cancellationToken);
        if (error is not null)
            return new(null, null, error);

        var managementKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var managementKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(managementKey)));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var code = codeGenerator.Generate();
            if (await repository.CodeExistsAsync(code, cancellationToken))
                continue;

            var link = new Link
            {
                Code = code,
                TargetUrl = targetUrl!,
                ManagementKeyHash = managementKeyHash
            };

            // A concurrent insert may claim the code after the existence check.
            if (await repository.TryAddAsync(link, cancellationToken))
                return new(code, managementKey, null);
        }
    }
}
