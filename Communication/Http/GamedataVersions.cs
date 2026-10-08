using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Plus.Communication.Http;

public interface IGamedataVersions
{
    // Current version (entity tag) per configured file. Files whose version is unknown are left out.
    Task<IReadOnlyDictionary<string, string>> Current();
}

// Looks up each configured file's entity tag with a HEAD request that bypasses CDN caches, so a file republished by
// any tool gets a new version at the next refresh. Versions are served from memory and refreshed in the background
// once older than RefreshSeconds; only the very first call waits for the lookups.
public sealed partial class GamedataVersions : IGamedataVersions, IDisposable
{
    private readonly GamedataVersionsConfiguration _configuration;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private (DateTimeOffset LoadedAt, IReadOnlyDictionary<string, string> Versions)? _current;
    private Task<IReadOnlyDictionary<string, string>>? _refresh;

    public GamedataVersions(IOptions<GamedataVersionsConfiguration> configuration, TimeProvider clock)
        : this(configuration, clock, new SocketsHttpHandler { UseProxy = false }) { }

    internal GamedataVersions(IOptions<GamedataVersionsConfiguration> configuration, TimeProvider clock, HttpMessageHandler handler)
    {
        _configuration = configuration.Value;
        _clock = clock;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<IReadOnlyDictionary<string, string>> Current()
    {
        if (!Uri.TryCreate(_configuration.BaseUrl, UriKind.Absolute, out var baseUrl) || _configuration.Files.Length == 0) {
            return new Dictionary<string, string>();
        }

        Task<IReadOnlyDictionary<string, string>> refresh;

        lock (_sync) {
            var fresh = _current is { } current && _clock.GetUtcNow() - current.LoadedAt < TimeSpan.FromSeconds(Math.Max(1, _configuration.RefreshSeconds));

            if (fresh) {
                return _current!.Value.Versions;
            }

            // Started off the lock: Refresh clears _refresh when it ends, which must happen after it is assigned.
            refresh = _refresh ??= Task.Run(() => Refresh(baseUrl));

            // Stale versions are still correct for files that did not change; serve them while refreshing.
            if (_current is { } stale) {
                return stale.Versions;
            }
        }

        return await refresh;
    }

    private async Task<IReadOnlyDictionary<string, string>> Refresh(Uri baseUrl)
    {
        try {
            var lookups = _configuration.Files.Distinct().Select(async file => (file, version: await Lookup(baseUrl, file)));
            var versions = (await Task.WhenAll(lookups))
                .Where(entry => entry.version != null)
                .ToDictionary(entry => entry.file, entry => entry.version!);

            lock (_sync) {
                _current = (_clock.GetUtcNow(), versions);
            }

            return versions;
        }
        finally {
            lock (_sync) {
                _refresh = null;
            }
        }
    }

    private async Task<string?> Lookup(Uri baseUrl, string file)
    {
        // A query string of its own keeps CDN edge caches out of the answer.
        var url = new Uri(baseUrl, Uri.EscapeDataString(file) + "?version-probe=" + _clock.GetUtcNow().ToUnixTimeMilliseconds());

        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, _configuration.TimeoutMilliseconds)));
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _http.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode) {
                return null;
            }

            var tag = response.Headers.ETag?.Tag?.Trim('"');

            return tag != null && VersionPattern().IsMatch(tag) ? tag : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException) {
            return null;
        }
    }

    // The refresh in progress, if any (tests wait for it).
    internal Task PendingRefresh
    {
        get
        {
            lock (_sync) {
                return _refresh ?? Task.CompletedTask;
            }
        }
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex VersionPattern();
}
