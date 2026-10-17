using LinkShortener.Api.Application;
using Microsoft.AspNetCore.Components;

namespace LinkShortener.Api.Components.Pages;

public partial class Home
{
    [Inject] protected LinkService LinkService { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromForm(FormName = "shorten-link")]
    protected ShortenLinkInput? Input { get; set; }

    protected string? Error { get; private set; }
    protected string? ShortUrl { get; private set; }
    protected string? ManagementKey { get; private set; }

    protected override void OnInitialized() => Input ??= new();

    protected async Task ShortenAsync()
    {
        Error = null;
        ShortUrl = null;
        ManagementKey = null;

        var result = await LinkService.CreateAsync(Input!.TargetUrl,
            string.IsNullOrEmpty(Input.Code) ? null : Input.Code);
        Error = result.Error;
        if (Error is not null)
            return;

        ShortUrl = new Uri(new Uri(Navigation.BaseUri), result.Code!).AbsoluteUri;
        ManagementKey = result.ManagementKey;
    }

    public sealed class ShortenLinkInput
    {
        public string? TargetUrl { get; set; }
        public string? Code { get; set; }
    }
}
