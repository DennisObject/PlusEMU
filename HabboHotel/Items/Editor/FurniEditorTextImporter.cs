using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Plus.HabboHotel.Items.Editor;

public interface IFurniEditorTextImporter
{
    // Official name and description for a classname, from FurniEditor:ImportUrl; null when the import is off or fails.
    Task<FurniEditorImportResult?> Find(string classname);
}

public sealed class FurniEditorTextImporter : IFurniEditorTextImporter, IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly FurniEditorConfiguration _configuration;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _fetch = new(1, 1);
    private (DateTime LoadedAt, Dictionary<string, (string Name, string Description)> Texts)? _cache;

    public FurniEditorTextImporter(IOptions<FurniEditorConfiguration> configuration)
    {
        _configuration = configuration.Value;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(_configuration.ImportTimeoutSeconds, 1, 120)) };
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

    private async Task<Dictionary<string, (string Name, string Description)>?> Texts()
    {
        if (!Uri.TryCreate(_configuration.ImportUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            return null;
        await _fetch.WaitAsync();
        try
        {
            if (_cache is { } cached && DateTime.UtcNow - cached.LoadedAt < CacheLifetime)
                return cached.Texts;
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > _configuration.FurnidataMaxBytes)
                return null;
            await using var body = await response.Content.ReadAsStreamAsync();
            using var limited = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(buffer)) > 0)
            {
                if (limited.Length + read > _configuration.FurnidataMaxBytes)
                    return null;
                limited.Write(buffer, 0, read);
            }
            var texts = Parse(limited.ToArray());
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
