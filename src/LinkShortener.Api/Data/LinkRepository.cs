using LinkShortener.Api.Application;
using LinkShortener.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LinkShortener.Api.Data;

public sealed class LinkRepository(LinkDbContext context) : ILinkRepository
{
    public async Task IncrementAccessCountAsync(Guid linkId, CancellationToken cancellationToken)
    {
        await context.Links.Where(link => link.Id == linkId)
            .ExecuteUpdateAsync(update => update.SetProperty(
                link => link.AccessCount, link => link.AccessCount + 1), cancellationToken);
    }

    public Task<Link?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Links.AsNoTracking().SingleOrDefaultAsync(link => link.Code == code, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        context.Links.AnyAsync(link => link.Code == code, cancellationToken);

    public async Task<bool> TryAddAsync(Link link, CancellationToken cancellationToken)
    {
        context.Links.Add(link);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            context.Entry(link).State = EntityState.Detached;
            return false;
        }
    }
}
