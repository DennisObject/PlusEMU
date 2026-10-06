using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Plus.HabboHotel.Items.Editor;

public interface IFurniEditorTextImporter
{
    bool IsConfigured { get; }

    // Official name and description for a classname, from FurniEditor:ImportUrl; null when the fetch fails.
    Task<FurniEditorImportResult?> Find(string classname);
}

// Fetches the official furnidata over https from allowlisted hosts only. Redirects are followed by hand so every hop
// is checked against the allowlist, and every connection goes to an address that was resolved and checked to be
// public, so a hostname cannot be pointed at the hotel's own network. One deadline covers waiting for another
// import, every hop and the whole body, and the body is capped.
public sealed class FurniEditorTextImporter : IFurniEditorTextImporter, IDisposable
{
    private const int MaxRedirects = 3;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly FurniEditorConfiguration _configuration;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _fetch = new(1, 1);
    private (DateTimeOffset LoadedAt, Dictionary<string, (string Name, string Description)> Texts)? _cache;

    public FurniEditorTextImporter(IOptions<FurniEditorConfiguration> configuration, TimeProvider clock)
        : this(configuration, clock, new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectCallback = ConnectPublic })
    {
    }

    internal FurniEditorTextImporter(IOptions<FurniEditorConfiguration> configuration, TimeProvider clock, HttpMessageHandler handler)
    {
        _configuration = configuration.Value;
        _clock = clock;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public bool IsConfigured => Uri.TryCreate(_configuration.ImportUrl, UriKind.Absolute, out _);

    public async Task<FurniEditorImportResult?> Find(string classname)
    {
        var texts = await Texts();

        if (texts == null) {
            return null;
        }

        return texts.TryGetValue(classname.Trim().ToLowerInvariant(), out var text)
            ? new(true, text.Name, text.Description, classname)
            : new(false, string.Empty, string.Empty, classname);
    }

    internal static bool IsAllowed(Uri url, IEnumerable<string> hosts)
    {
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || url.Port != 443 || !string.IsNullOrEmpty(url.UserInfo)) {
            return false;
        }

        var host = url.IdnHost.TrimEnd('.').ToLowerInvariant();

        return hosts.Any(allowed => host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal));
    }

    // Public unicast only: no loopback, private, link-local, carrier-grade NAT, unique-local, multicast or unspecified.
    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast)) {
            return false;
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork) {
            return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168));
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC);
    }

    // The address actually connected to is the one checked here, so DNS cannot change between check and connect.
    internal static async Task<IPAddress> ResolvePublic(string host, CancellationToken cancellation)
    {
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellation);

        return addresses.FirstOrDefault(IsPublic) is { } address && addresses.All(IsPublic)
            ? address
            : throw new HttpRequestException($"{host} does not resolve to public addresses only");
    }

    private static async ValueTask<Stream> ConnectPublic(SocketsHttpConnectionContext context, CancellationToken cancellation)
    {
        var address = await ResolvePublic(context.DnsEndPoint.Host, cancellation);
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellation);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch {
            socket.Dispose();
            throw;
        }
    }

    private async Task<Dictionary<string, (string Name, string Description)>?> Texts()
    {
        if (!Uri.TryCreate(_configuration.ImportUrl, UriKind.Absolute, out var url)) {
            return null;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(_configuration.ImportTimeoutSeconds, 1, 30)));

        try {
            await _fetch.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) {
            return null;
        }

        try {
            if (_cache is { } cached && _clock.GetUtcNow() - cached.LoadedAt < CacheLifetime) {
                return cached.Texts;
            }

            var body = await Download(url, deadline.Token);

            if (body == null) {
                return null;
            }

            var texts = Parse(body);
            _cache = (_clock.GetUtcNow(), texts);

            return texts;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or IOException) {
            return null;
        }
        finally {
            _fetch.Release();
        }
    }

    private async Task<byte[]?> Download(Uri url, CancellationToken cancellation)
    {
        for (int hop = 0; hop <= MaxRedirects; hop++) {
            if (!IsAllowed(url, _configuration.AllowedImportHosts)) {
                return null;
            }

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if ((int)response.StatusCode is >= 300 and < 400) {
                if (response.Headers.Location is not { } location) {
                    return null;
                }

                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }

            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > _configuration.ImportMaxBytes) {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            using var body = new MemoryStream();
            var buffer = new byte[81920];
            int read;

            while ((read = await stream.ReadAsync(buffer, cancellation)) > 0) {
                if (body.Length + read > _configuration.ImportMaxBytes) {
                    return null;
                }

                body.Write(buffer, 0, read);
            }

            return body.ToArray();
        }

        return null;
    }

    internal static Dictionary<string, (string Name, string Description)> Parse(byte[] json)
    {
        var texts = new Dictionary<string, (string Name, string Description)>();
        using var document = JsonDocument.Parse(json);

        foreach (var section in new[] { "roomitemtypes", "wallitemtypes" }) {
            if (!document.RootElement.TryGetProperty(section, out var element) || !element.TryGetProperty("furnitype", out var types)
                || types.ValueKind != JsonValueKind.Array) {
                continue;
            }

            foreach (var entry in types.EnumerateArray()) {
                if (entry.TryGetProperty("classname", out var name) && name.ValueKind == JsonValueKind.String) {
                    texts.TryAdd(name.GetString()!.Trim().ToLowerInvariant(), (String(entry, "name"), String(entry, "description")));
                }
            }
        }

        return texts;
    }

    private static string String(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    public void Dispose()
    {
        _http.Dispose();
        _fetch.Dispose();
    }
}
