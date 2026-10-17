using System.Net;
using System.Net.Sockets;
using LinkShortener.Api.Application;
using Xunit;

namespace LinkShortener.Tests.Application;

public sealed class TargetUrlValidatorTests
{
    private static TargetUrlValidator CreateValidator(params string[] addresses) =>
        new((_, _) => Task.FromResult(addresses.Select(IPAddress.Parse).ToArray()));

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?name=value#section")]
    [InlineData("HTTPS://example.com:8443/path")]
    [InlineData("https://example.com/a%20b")]
    public async Task ValidateAcceptsHttpAndHttpsWithPublicAddresses(string targetUrl)
    {
        var validator = CreateValidator("93.184.215.14", "2606:4700:4700::1111");

        Assert.Null(await validator.ValidateAsync(targetUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("example.com")]
    [InlineData("/relative/path")]
    [InlineData("//example.com/path")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://")]
    [InlineData("https:///path")]
    [InlineData("https://exa mple.com")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com:99999")]
    [InlineData(" https://example.com")]
    [InlineData("https://example.com\n")]
    public async Task ValidateRejectsMissingMalformedOrUnsupportedUrlsBeforeDns(string? targetUrl)
    {
        var validator = new TargetUrlValidator((_, _) =>
            throw new InvalidOperationException("DNS não deve ser consultado para uma URL inválida."));

        Assert.NotNull(await validator.ValidateAsync(targetUrl));
    }

    [Theory]
    [InlineData(2047, true)]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public async Task ValidateEnforces2048CharacterLimit(int length, bool expectedValid)
    {
        const string prefix = "https://example.com/";
        var targetUrl = prefix + new string('a', length - prefix.Length);
        var validator = CreateValidator("93.184.215.14");

        var error = await validator.ValidateAsync(targetUrl);

        Assert.Equal(expectedValid, error is null);
        if (!expectedValid)
        {
            Assert.Equal("A URL de destino deve ter no máximo 2048 caracteres.", error);
        }
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("https://LOCALHOST./path")]
    [InlineData("https://app.localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.1")]
    [InlineData("http://2130706433")]
    [InlineData("http://0x7f000001")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://169.254.169.254")]
    [InlineData("http://100.64.0.1")]
    [InlineData("http://0.0.0.0")]
    [InlineData("http://192.0.2.1")]
    [InlineData("http://198.18.0.1")]
    [InlineData("http://224.0.0.1")]
    [InlineData("http://255.255.255.255")]
    [InlineData("http://[::1]")]
    [InlineData("http://[::]")]
    [InlineData("http://[fc00::1]")]
    [InlineData("http://[fe80::1]")]
    [InlineData("http://[ff02::1]")]
    [InlineData("http://[::ffff:127.0.0.1]")]
    [InlineData("http://[2001:db8::1]")]
    [InlineData("http://[2002:7f00:1::]")]
    public async Task ValidateRejectsNonPublicLiteralHostsWithoutDns(string targetUrl)
    {
        var validator = new TargetUrlValidator((_, _) =>
            throw new InvalidOperationException("DNS não deve ser consultado para um host literal."));

        Assert.Equal("A URL de destino deve ser publicamente acessível.",
            await validator.ValidateAsync(targetUrl));
    }

    [Theory]
    [InlineData("https://8.8.8.8")]
    [InlineData("http://[2606:4700:4700::1111]")]
    [InlineData("http://[::ffff:8.8.8.8]")]
    public async Task ValidateAcceptsPublicLiteralHostsWithoutDns(string targetUrl)
    {
        var validator = new TargetUrlValidator((_, _) =>
            throw new InvalidOperationException("DNS não deve ser consultado para um host literal."));

        Assert.Null(await validator.ValidateAsync(targetUrl));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("::ffff:192.168.1.1")]
    public async Task ValidateRejectsDomainsWhenAnyResolvedAddressIsNonPublic(string privateAddress)
    {
        var validator = CreateValidator("93.184.215.14", privateAddress);

        Assert.Equal("A URL de destino deve ser publicamente acessível.",
            await validator.ValidateAsync("https://example.com"));
    }

    [Fact]
    public async Task ValidateRejectsDomainsWithoutAddresses()
    {
        var validator = CreateValidator();

        Assert.Equal("A URL de destino deve ser publicamente acessível.",
            await validator.ValidateAsync("https://example.com"));
    }

    [Fact]
    public async Task ValidateReturnsPortugueseErrorWhenDnsFails()
    {
        var validator = new TargetUrlValidator((_, _) =>
            throw new SocketException((int)SocketError.HostNotFound));

        Assert.Equal("A URL de destino deve ser publicamente acessível.",
            await validator.ValidateAsync("https://example.invalid"));
    }

    [Fact]
    public async Task ValidatePassesHostAndCancellationTokenToResolver()
    {
        using var cancellation = new CancellationTokenSource();
        var validator = new TargetUrlValidator((host, token) =>
        {
            Assert.Equal("example.com", host);
            Assert.Equal(cancellation.Token, token);
            return Task.FromResult(new[] { IPAddress.Parse("93.184.215.14") });
        });

        Assert.Null(await validator.ValidateAsync("https://example.com/path", cancellation.Token));
    }

    [Fact]
    public async Task ValidatePropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var validator = new TargetUrlValidator((_, token) => Task.FromCanceled<IPAddress[]>(token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateAsync("https://example.com", cancellation.Token));
    }
}
