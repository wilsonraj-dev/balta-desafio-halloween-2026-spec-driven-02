using System.Net;
using LinkShortener.Api.Application;
using LinkShortener.Api.Components.Pages;
using LinkShortener.Api.Domain;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LinkShortener.Tests.Components;

public sealed class HomeTests
{
    [Fact]
    public async Task HomeRendersShorteningFormWithAccessibleFields()
    {
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton(new LinkService(new FakeRepository(), new LinkCodeGenerator(), new TargetUrlValidator()))
            .AddSingleton<NavigationManager>(new TestNavigationManager()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<Home>();
            return component.ToHtmlString();
        });

        Assert.Contains("<main", html);
        var content = WebUtility.HtmlDecode(html);
        Assert.Contains("Menos caracteres, mais conexões.", content);
        Assert.Contains("URL de destino", content);
        Assert.Contains("Código próprio (opcional)", content);
        Assert.Contains("Encurtar link", content);
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("for=\"target-url\"", html);
        Assert.Contains("for=\"custom-code\"", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CUSTOM1")]
    public async Task SubmitCreatesLinkAndDisplaysAbsoluteUrlAndManagementKey(string? code)
    {
        var repository = new FakeRepository();
        var page = new TestHome(repository);

        await page.SubmitAsync("https://8.8.8.8/path", code);

        var link = Assert.Single(repository.Saved);
        Assert.Equal("https://8.8.8.8/path", link.TargetUrl);
        Assert.Equal("https://short.example/app/" + link.Code, page.CreatedUrl);
        Assert.Matches("^[A-F0-9]{64}$", page.CreatedKey!);
        Assert.Null(page.Message);
        if (!string.IsNullOrEmpty(code))
            Assert.Equal(code, link.Code);
    }

    [Theory]
    [InlineData(null, null, "A URL de destino é obrigatória.")]
    [InlineData("ftp://8.8.8.8/", null, "protocolo http ou https")]
    [InlineData("https://8.8.8.8/", "abc", "O código próprio deve ter")]
    public async Task InvalidSubmissionDisplaysServiceErrorWithoutCreatingLink(
        string? targetUrl, string? code, string expectedError)
    {
        var repository = new FakeRepository();
        var page = new TestHome(repository);

        await page.SubmitAsync(targetUrl, code);

        Assert.Contains(expectedError, page.Message);
        Assert.Null(page.CreatedUrl);
        Assert.Null(page.CreatedKey);
        Assert.Empty(repository.Saved);
    }

    [Fact]
    public async Task DuplicateCodeDisplaysConflictAndClearsPreviousResult()
    {
        var repository = new FakeRepository();
        var page = new TestHome(repository);
        await page.SubmitAsync("https://8.8.8.8/", "CUSTOM1");

        await page.SubmitAsync("https://8.8.8.8/other", "CUSTOM1");

        Assert.Equal("O código informado já está em uso.", page.Message);
        Assert.Null(page.CreatedUrl);
        Assert.Null(page.CreatedKey);
        Assert.Single(repository.Saved);
    }

    private sealed class TestHome : Home
    {
        public TestHome(FakeRepository repository)
        {
            LinkService = new(repository, new LinkCodeGenerator(), new TargetUrlValidator());
            Navigation = new TestNavigationManager();
        }

        public string? CreatedUrl => ShortUrl;
        public string? CreatedKey => ManagementKey;
        public string? Message => Error;

        public Task SubmitAsync(string? targetUrl, string? code)
        {
            Input = new() { TargetUrl = targetUrl, Code = code };
            return ShortenAsync();
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://short.example/app/", "https://short.example/app/");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class FakeRepository : ILinkRepository
    {
        public List<Link> Saved { get; } = [];

        public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.Any(link => link.Code == code));

        public Task<bool> TryAddAsync(Link link, CancellationToken cancellationToken)
        {
            Saved.Add(link);
            return Task.FromResult(true);
        }

        public Task<Link?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task IncrementAccessCountAsync(Guid linkId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
