using LinkShortener.Api.Domain;

namespace LinkShortener.Api.Application;

public interface ILinkRepository
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);
    Task<bool> TryAddAsync(Link link, CancellationToken cancellationToken);
}
