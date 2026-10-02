using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Plus.HabboHotel.Camera;

internal sealed record CameraRenderedImage(byte[] Png, byte[] SmallPng);
internal sealed class CameraRendererClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CameraConfiguration _options;
    private readonly SemaphoreSlim _capacity;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public CameraRendererClient(IOptions<CameraConfiguration> options)
    {
        _options = options.Value;
        if (_options.MaxConcurrency is < 1 or > 4 || _options.TimeoutSeconds is < 1 or > 25) throw new InvalidOperationException("Invalid camera resource limits");
        _capacity = new(_options.MaxConcurrency);
        _http = new(CreateHandler()) { Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds) };
    }
    internal static SocketsHttpHandler CreateHandler() => new()
        {
            AllowAutoRedirect = false, UseProxy = false,
            ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                foreach (var address in addresses.Where(IsPrivateAddress))
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try { await socket.ConnectAsync(address, context.DnsEndPoint.Port, token); return new NetworkStream(socket, ownsSocket: true); }
                    catch { socket.Dispose(); }
                }
                throw new HttpRequestException("Camera renderer has no reachable private address");
            }
        };
    internal static bool IsPrivateAddress(IPAddress address) =>
        CameraEffectCatalogue.IsPrivateHttp(new Uri($"http://{(address.AddressFamily == AddressFamily.InterNetworkV6 ? "["+address+"]" : address.ToString())}/render"));

    public async Task<CameraRenderedImage> Render(JsonElement scene, CameraViewport viewport, IReadOnlyList<CameraEffectSelection> effects, bool zoom, int level, CancellationToken token)
    {
        if (_options.Bearer.Length < 32 || !Uri.TryCreate(_options.RendererUrl, UriKind.Absolute, out var uri) ||
            !CameraEffectCatalogue.IsPrivateHttp(uri) || uri.AbsolutePath != "/render" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Camera renderer is not configured");
        if (!await _capacity.WaitAsync(0, token)) throw new InvalidOperationException("Camera renderer is busy");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            token = deadline.Token;
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { scene, viewport, effects, zoom, level }, Json);
            if (body.Length > 1024 * 1024) throw new InvalidOperationException("Camera room is too large");
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Bearer);
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json" ||
                response.Content.Headers.ContentLength > 6 * 1024 * 1024) throw new InvalidOperationException("Camera renderer did not return an image");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int length;
            while ((length = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + length > 6 * 1024 * 1024) throw new InvalidOperationException("Camera response is too large");
                output.Write(buffer, 0, length);
            }
            using var json = JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2) throw new InvalidOperationException("Invalid renderer response");
            return new(Decode(root.GetProperty("png").GetString(), viewport.CropWidth), Decode(root.GetProperty("smallPng").GetString(), 110));
        }
        finally { _capacity.Release(); }
    }
    internal static byte[] Decode(string? value, int size)
    {
        if (value == null || value.Length > 2_796_204) throw new InvalidOperationException("Invalid camera PNG");
        var bytes = Convert.FromBase64String(value);
        ReadOnlySpan<byte> magic = [137,80,78,71,13,10,26,10];
        if (bytes.Length < 33 || bytes.Length > 2 * 1024 * 1024 || !bytes.AsSpan(0,8).SequenceEqual(magic) ||
            !bytes.AsSpan(12,4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16,4)) != size ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20,4)) != size) throw new InvalidOperationException("Invalid camera PNG dimensions");
        return bytes;
    }
    public void Dispose() { _http.Dispose(); _capacity.Dispose(); }
}
