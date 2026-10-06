using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plus.HabboHotel.Camera;

public sealed record CameraEffectDefinition(string Name, int MinLevel, string Type);

public interface ICameraEffectCatalogue
{
    IReadOnlyList<CameraEffectDefinition> Current();
}

public sealed class CameraEffectCatalogue : ICameraEffectCatalogue, IDisposable
{
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "colormatrix", "frame", "composite" };

    private readonly CameraConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<CameraEffectCatalogue> _logger;
    private readonly HttpClient _http;
    private readonly Lock _gate = new();
    private IReadOnlyList<CameraEffectDefinition> _effects = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public CameraEffectCatalogue(IOptions<CameraConfiguration> options, TimeProvider time, ILogger<CameraEffectCatalogue> logger)
        : this(options, time, logger, CameraRendererClient.CreateHandler()) { }

    internal CameraEffectCatalogue(IOptions<CameraConfiguration> options, TimeProvider time, ILogger<CameraEffectCatalogue> logger, HttpMessageHandler handler)
    {
        _configuration = options.Value;
        _time = time;
        _logger = logger;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
    }

    public IReadOnlyList<CameraEffectDefinition> Current()
    {
        lock (_gate) {
            if (_time.GetUtcNow() - _loadedAt < RefreshAfter) {
                return _effects;
            }

            var refreshed = Load();
            _loadedAt = _time.GetUtcNow();
            _effects = refreshed ?? [];

            return _effects;
        }
    }

    internal static IReadOnlyList<CameraEffectDefinition>? Parse(string json)
    {
        try {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) {
                return null;
            }

            var effects = new List<CameraEffectDefinition>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            while (reader.Read()) {
                if (reader.TokenType == JsonTokenType.EndArray) {
                    return reader.Read() ? null : effects;
                }

                if (!TryEffect(ref reader, out var effect) || !names.Add(effect.Name)) {
                    return null;
                }

                if (effects.Count >= 1000) {
                    return null;
                }

                effects.Add(effect);
            }
        }
        catch (JsonException) {
            return null;
        }

        return null;
    }

    private IReadOnlyList<CameraEffectDefinition>? Load()
    {
        if (_configuration.Bearer.Length < 32 || !Uri.TryCreate(_configuration.RendererUrl, UriKind.Absolute, out var render)) {
            return [];
        }

        var effects = new UriBuilder(render) { Path = "/effects", Query = "", Fragment = "" }.Uri;

        if (!IsPrivateHttp(render) || render.AbsolutePath != "/render" || render.Query.Length != 0 || render.Fragment.Length != 0 || !IsPrivateHttp(effects)) {
            return [];
        }

        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, effects);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.Bearer);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).GetAwaiter().GetResult();

            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength > 65536) {
                return null;
            }

            using var stream = response.Content.ReadAsStreamAsync(deadline.Token).GetAwaiter().GetResult();
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int length;

            while ((length = stream.ReadAsync(buffer, deadline.Token).AsTask().GetAwaiter().GetResult()) > 0) {
                if (output.Length + length > 65536) {
                    return [];
                }

                output.Write(buffer, 0, length);
            }

            return Parse(Encoding.UTF8.GetString(output.ToArray())) ?? [];
        }
        catch (Exception) {
            _logger.LogWarning("Camera effect catalogue is unavailable");

            return null;
        }
    }

    private static bool TryEffect(ref Utf8JsonReader reader, out CameraEffectDefinition effect)
    {
        effect = new("", 0, "");

        if (reader.TokenType != JsonTokenType.StartObject) {
            return false;
        }

        string? name = null;
        int? level = null;
        string? type = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject) {
            if (reader.TokenType != JsonTokenType.PropertyName) {
                return false;
            }

            var property = reader.GetString() ?? "";

            if (!seen.Add(property) || !reader.Read()) {
                return false;
            }

            switch (property) {
                case "name" when reader.TokenType == JsonTokenType.String:
                    name = reader.GetString();
                    break;
                case "minLevel" when reader.TokenType == JsonTokenType.Number
                    && int.TryParse(Encoding.UTF8.GetString(reader.ValueSpan), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    && parsed is >= 0 and <= 10:
                    level = parsed;
                    break;
                case "type" when reader.TokenType == JsonTokenType.String && Types.Contains(reader.GetString() ?? ""):
                    type = reader.GetString();
                    break;
                default:
                    return false;
            }
        }

        if (name == null || level == null || type == null || !IsEffectName(name)) {
            return false;
        }

        effect = new CameraEffectDefinition(name, level.Value, type);

        return true;
    }

    public void Dispose() => _http.Dispose();

    private static bool IsEffectName(string name) =>
        name.Length > 0 && name.Length <= 64 && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_') &&
        !name.Contains("://", StringComparison.Ordinal) && !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPrivateHttp(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "http" || !string.IsNullOrEmpty(uri.UserInfo)) {
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (uri.HostNameType == UriHostNameType.Dns) {
            return !uri.IdnHost.Contains('.');
        }

        if (!IPAddress.TryParse(uri.IdnHost, out var address)) {
            return false;
        }

        if (IPAddress.IsLoopback(address)) {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
            var bytes = address.GetAddressBytes();

            return bytes[0] == 10 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31);
        }

        var ipv6 = address.GetAddressBytes();

        return address.IsIPv6LinkLocal || (ipv6[0] & 0xFE) == 0xFC;
    }
}
