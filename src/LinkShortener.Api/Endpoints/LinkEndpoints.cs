using LinkShortener.Api.Application;

namespace LinkShortener.Api.Endpoints;

public sealed record CreateLinkRequest(string? TargetUrl);
public sealed record CreateLinkResponse(string ShortUrl, string Code, string ManagementKey);

public static class LinkEndpoints
{
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/links", async (
            CreateLinkRequest request, LinkService service, HttpRequest httpRequest,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request.TargetUrl, cancellationToken);
            if (result.Error is not null)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "URL de destino inválida.", detail: result.Error);

            var shortUrl = $"{httpRequest.Scheme}://{httpRequest.Host}{httpRequest.PathBase}/{result.Code}";
            return Results.Created(shortUrl,
                new CreateLinkResponse(shortUrl, result.Code!, result.ManagementKey!));
        });

        return endpoints;
    }
}
