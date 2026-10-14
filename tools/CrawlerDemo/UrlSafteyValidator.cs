using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;


namespace CrawlerDemo;

// The result of a safety check. Carrying a Reason lets us log/echo WHY a URL was
// rejected — useful for debugging and for telling a user why their link was skipped.
public readonly record struct UrlSafetyResult(bool IsAllowed, string? Reason)
{
    public static UrlSafetyResult Allow() => new(true, null);
    public static UrlSafetyResult Deny(string reason) => new(false, reason);
}

// Protects the crawler against SSRF: requests that trick our server into contacting
// internal/reserved addresses it can reach but the attacker cannot.
public sealed class UrlSafetyValidator
{
    // DNS lookup is injectable so tests can simulate "this hostname resolves to a
    // private IP" without touching the real network. Defaults to real DNS.
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveDns;

    public UrlSafetyValidator(Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null)
    {
        _resolveDns = dnsResolver ?? (static (host, ct) => Dns.GetHostAddressesAsync(host, ct));
    }

    public async Task<UrlSafetyResult> ValidateAsync(Uri uri, CancellationToken ct = default)
    {
        // --- Layer 1: protocol allow-list ---
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return UrlSafetyResult.Deny($"Protocol '{uri.Scheme}' not allowed (http/https only).");

        var host = uri.DnsSafeHost;
        if (string.IsNullOrWhiteSpace(host))
            return UrlSafetyResult.Deny("URL has no host.");

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return UrlSafetyResult.Deny("Host 'localhost' not allowed.");

        // --- Resolve to IPs (the host might already be a literal IP) ---
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = await _resolveDns(host, ct);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                return UrlSafetyResult.Deny($"DNS resolution failed for '{host}'.");
            }
            if (addresses.Length == 0)
                return UrlSafetyResult.Deny($"Host '{host}' resolved to no addresses.");
        }

        // --- Layer 2: EVERY resolved IP must be public. One bad one = reject all. ---
        foreach (var addr in addresses)
        {
            if (!IsPublicAddress(addr, out var why))
                return UrlSafetyResult.Deny($"'{host}' resolves to disallowed {addr}: {why}");
        }

        return UrlSafetyResult.Allow();
    }

    // The core predicate: is this IP safe to contact over the public internet?
    public static bool IsPublicAddress(IPAddress address, out string reason)
    {
        // Normalize IPv4-mapped IPv6 (::ffff:127.0.0.1) back to IPv4 so v4 rules apply.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) { reason = "loopback"; return false; }

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsPublicIPv4(address, out reason);
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return IsPublicIPv6(address, out reason);

        reason = "unknown address family";
        return false;
    }

    private static bool IsPublicIPv4(IPAddress address, out string reason)
    {
        var b = address.GetAddressBytes();   // b[0] is the first octet

        if (b[0] == 0)                                  { reason = "0.0.0.0/8";            return false; }
        if (b[0] == 10)                                 { reason = "private 10/8";         return false; }
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   { reason = "CGNAT 100.64/10";      return false; }
        if (b[0] == 127)                                { reason = "loopback 127/8";       return false; }
        if (b[0] == 169 && b[1] == 254)                 { reason = "link-local 169.254/16 (cloud metadata)"; return false; }
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    { reason = "private 172.16/12";    return false; }
        if (b[0] == 192 && b[1] == 168)                 { reason = "private 192.168/16";   return false; }
        if (b[0] >= 224)                                { reason = "multicast/reserved";   return false; }

        reason = "public";
        return true;
    }

    private static bool IsPublicIPv6(IPAddress address, out string reason)
    {
        if (address.IsIPv6LinkLocal) { reason = "IPv6 link-local fe80::/10"; return false; }
        if (address.IsIPv6SiteLocal) { reason = "IPv6 site-local";          return false; }
        if (address.IsIPv6Multicast) { reason = "IPv6 multicast";           return false; }

        var b = address.GetAddressBytes();
        if (b.All(x => x == 0))        { reason = "unspecified ::";          return false; }
        if ((b[0] & 0xFE) == 0xFC)     { reason = "unique-local fc00::/7";   return false; }

        reason = "public";
        return true;
    }
}