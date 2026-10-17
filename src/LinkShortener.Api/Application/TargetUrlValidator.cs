using System.Net;
using System.Net.Sockets;

namespace LinkShortener.Api.Application;

public sealed class TargetUrlValidator
{
    private const int MaxUrlLength = 2048;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveHost;

    private static readonly IPNetwork[] NonPublicNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("2001::/23"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("3fff::/20")
    ];

    private static readonly IPNetwork GlobalIpv6Network = IPNetwork.Parse("2000::/3");

    public TargetUrlValidator()
        : this((host, cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken))
    {
    }

    public TargetUrlValidator(Func<string, CancellationToken, Task<IPAddress[]>> resolveHost)
    {
        ArgumentNullException.ThrowIfNull(resolveHost);
        this.resolveHost = resolveHost;
    }

    // A null result means validation succeeded; otherwise the result is the validation error.
    public async Task<string?> ValidateAsync(string? targetUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return "A URL de destino é obrigatória.";
        }

        if (targetUrl.Length > MaxUrlLength)
        {
            return "A URL de destino deve ter no máximo 2048 caracteres.";
        }

        if (targetUrl.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri)
            || !uri.IsWellFormedOriginalString()
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return "A URL de destino deve ser uma URL absoluta válida com protocolo http ou https.";
        }

        const string publicAccessError = "A URL de destino deve ser publicamente acessível.";
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return publicAccessError;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return IsPublicAddress(address) ? null : publicAccessError;
        }

        try
        {
            var addresses = await resolveHost(host, cancellationToken);
            return addresses.Length > 0 && addresses.All(IsPublicAddress) ? null : publicAccessError;
        }
        catch (SocketException)
        {
            return publicAccessError;
        }
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6
            && (address.ScopeId != 0 || !GlobalIpv6Network.Contains(address)))
        {
            return false;
        }

        return !NonPublicNetworks.Any(network => network.Contains(address));
    }
}
