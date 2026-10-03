using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Plus.HabboHotel.Items.Editor;

public interface IFurniEditorTextImporter
{
    // Official name and description for a classname, from FurniEditor:ImportUrl; null when the import is off or fails.
    Task<FurniEditorImportResult?> Find(string classname);
}

// Fetches the official furnidata over https from allowlisted hosts only. Redirects are followed by hand so every hop
// is checked against the allowlist; the body is capped and the whole request has a short timeout.
public sealed class FurniEditorTextImporter : IFurniEditorTextImporter, IDisposable
{
    private const int MaxRedirects = 3;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly FurniEditorConfiguration _configuration;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _fetch = new(1, 1);
    private (DateTime LoadedAt, Dictionary<string, (string Name, string Description)> Texts)? _cache;

    public FurniEditorTextImporter(IOptions<FurniEditorConfiguration> configuration)
        : this(configuration, new HttpClientHandler { AllowAutoRedirect = false }) { }

    internal FurniEditorTextImporter(IOptions<FurniEditorConfiguration> configuration, HttpMessageHandler handler)
    {
        _configuration = configuration.Value;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Clamp(_configuration.ImportTimeoutSeconds, 1, 30)) };
    }

    public async Task<FurniEditorImportResult?> Find(string classname)
    {
        var texts = await Texts();
        if (texts == null)
            return null;
        return texts.TryGetValue(classname.Trim().ToLowerInvariant(), out var text)
            ? new(true, text.Name, text.Description, classname)
            : new(false, string.Empty, string.Empty, classname);
    }

    internal static bool IsAllowed(Uri url, IEnumerable<string> hosts)
    {
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || url.Port != 443 || !string.IsNullOrEmpty(url.UserInfo))
            return false;
        var host = url.IdnHost.TrimEnd('.').ToLowerInvariant();
        return hosts.Any(allowed => host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal));
    }

    private async Task<Dictionary<string, (string Name, string Description)>?> Texts()
    {
        if (!Uri.TryCreate(_configuration.ImportUrl, UriKind.Absolute, out var url))
            return null;
        await _fetch.WaitAsync();
        try
        {
            if (_cache is { } cached && DateTime.UtcNow - cached.LoadedAt < CacheLifetime)
                return cached.Texts;
            var body = await Download(url);
            if (body == null)
                return null;
            var texts = Parse(body);
            _cache = (DateTime.UtcNow, texts);
            return texts;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
        finally
        {
            _fetch.Release();
        }
    }

    private async Task<byte[]?> Download(Uri url)
    {
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowed(url, _configuration.AllowedImportHosts))
                return null;
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (response.Headers.Location is not { } location)
                    return null;
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > _configuration.ImportMaxBytes)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var body = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                if (body.Length + read > _configuration.ImportMaxBytes)
                    return null;
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
        foreach (var section in new[] { "roomitemtypes", "wallitemtypes" })
        {
            if (!document.RootElement.TryGetProperty(section, out var element) || !element.TryGetProperty("furnitype", out var types)
                || types.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var entry in types.EnumerateArray())
            {
                if (entry.TryGetProperty("classname", out var name) && name.ValueKind == JsonValueKind.String)
                    texts.TryAdd(name.GetString()!.Trim().ToLowerInvariant(), (String(entry, "name"), String(entry, "description")));
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
