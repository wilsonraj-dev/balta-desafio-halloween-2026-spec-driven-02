using LinkShortener.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LinkShortener.Api.Data;

public sealed class LinkDbContext(DbContextOptions<LinkDbContext> options) : DbContext(options)
{
    public DbSet<Link> Links => Set<Link>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LinkDbContext).Assembly);
    }
}
