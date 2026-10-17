using LinkShortener.Api.Application;

namespace LinkShortener.Api.Endpoints;

public sealed record CreateLinkRequest(string? TargetUrl, string? Code = null);
public sealed record CreateLinkResponse(string ShortUrl, string Code, string ManagementKey);
public sealed record LinkStatsResponse(string Code, long AccessCount);

public static class LinkEndpoints
{
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/links", async (
            CreateLinkRequest request, LinkService service, HttpRequest httpRequest,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request.TargetUrl, request.Code, cancellationToken);
            if (result.Error is not null)
                return Results.Problem(
                    statusCode: result.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
                    title: result.IsConflict ? "Código já em uso." : "Requisição inválida.", detail: result.Error);

            var shortUrl = $"{httpRequest.Scheme}://{httpRequest.Host}{httpRequest.PathBase}/{result.Code}";
            return Results.Created(shortUrl,
                new CreateLinkResponse(shortUrl, result.Code!, result.ManagementKey!));
        });

        endpoints.MapGet("/api/links/{code}/stats", async (
            string code, LinkService service, HttpRequest request, CancellationToken cancellationToken) =>
        {
            var keys = request.Headers["X-Management-Key"];
            var result = await service.GetStatsAsync(code, keys.Count == 1 ? keys[0] : null, cancellationToken);
            return result.Status switch
            {
                LinkStatsStatus.Found => Results.Ok(new LinkStatsResponse(result.Code!, result.AccessCount!.Value)),
                LinkStatsStatus.Unauthorized => Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized, title: "Chave de gerenciamento inválida."),
                _ => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound, title: "O link não existe.")
            };
        });

        endpoints.MapGet("/{code}", async (
            string code, LinkService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ResolveAsync(code, cancellationToken);
            return result.Status switch
            {
                ResolveLinkStatus.Found => Results.Redirect(result.TargetUrl!),
                ResolveLinkStatus.Expired => Results.Problem(
                    statusCode: StatusCodes.Status410Gone, title: "O link expirou."),
                _ => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound, title: "O link não existe.")
            };
        });

        return endpoints;
    }
}
