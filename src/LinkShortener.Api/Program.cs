using LinkShortener.Api.Application;
using LinkShortener.Api.Data;
using LinkShortener.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var app = Program.CreateApp(args);
app.Run();

public partial class Program
{
    public static WebApplication CreateApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var databasePath = Path.GetFullPath(
            Path.Combine(builder.Environment.ContentRootPath, "..", "storage", "links.db"));
        builder.Services.AddDbContext<LinkDbContext>(options => options.UseSqlite(
            builder.Configuration.GetConnectionString("Links") ?? $"Data Source={databasePath}"));
        builder.Services.AddScoped<ILinkRepository, LinkRepository>();
        builder.Services.AddSingleton<ILinkCodeGenerator, LinkCodeGenerator>();
        builder.Services.AddSingleton<TargetUrlValidator>();
        builder.Services.AddSingleton<LinkRedirectCache>();
        builder.Services.AddScoped<LinkService>();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (context.ProblemDetails.Status == StatusCodes.Status400BadRequest)
                context.ProblemDetails.Title = "Requisição inválida.";
        });

        var app = builder.Build();
        app.UseStatusCodePages();
        app.MapLinkEndpoints();
        return app;
    }
}
