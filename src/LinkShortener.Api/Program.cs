using LinkShortener.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databasePath = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "storage", "links.db"));
builder.Services.AddDbContext<LinkDbContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Links") ?? $"Data Source={databasePath}"));

var app = builder.Build();

app.Run();
