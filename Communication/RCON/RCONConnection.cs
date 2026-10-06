using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Plus.Communication.RCON.Commands;

namespace Plus.Communication.RCON;

public sealed class RconConnection : IDisposable
{
    internal const int MaxRequestBytes = 4096;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly HashSet<string> AcknowledgedCommands = new(StringComparer.Ordinal)
    {
        "alert_user", "disconnect_user", "give_user_badge", "give_user_currency",
        "reload_catalog", "reload_filter", "reload_user_motto", "reload_user_rank", "take_user_currency",
    };

    private readonly ILogger<RconConnection> _logger;
    private readonly ICommandManager? _commands;
    private readonly byte[] _buffer = new byte[1024];
    private readonly object _gate = new();
    private readonly MemoryStream _request = new();
    private readonly Timer _timeout;
    private Socket? _socket;
    private bool _jsonRequest;
    private RequestState _state;
    private int _disposed;

    public RconConnection(Socket socket, ILogger<RconConnection> logger) : this(socket, logger, null)
    {
    }

    internal RconConnection(Socket socket, ILogger<RconConnection> logger, ICommandManager? commands)
    {
        _socket = socket;
        _logger = logger;
        _commands = commands;
        _timeout = new Timer(_ => TimeoutRequest(), null, RequestTimeout, Timeout.InfiniteTimeSpan);
        Receive();
    }

    private void Receive()
    {
        try {
            _socket?.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, OnCallBack, null);
        }
        catch {
            Dispose();
        }
    }

    internal void OnCallBack(IAsyncResult asyncResult)
    {
        try {
            var socket = _socket;

            if (socket == null) {
                return;
            }

            var bytes = socket.EndReceive(asyncResult);

            lock (_gate) {
                if (_state != RequestState.Pending) {
                    return;
                }

                if (bytes == 0) {
                    if (_jsonRequest && _request.Length > 0) {
                        ProcessJsonRequest();
                    }
                    else {
                        Dispose();
                    }

                    return;
                }

                if (_request.Length == 0) {
                    var marker = FirstNonWhitespace(_buffer.AsSpan(0, bytes));

                    if (marker == 0) {
                        _jsonRequest = true;
                        _request.Write(_buffer, 0, bytes);
                        Receive();

                        return;
                    }

                    _jsonRequest = marker == (byte)'{';
                }

                if (!_jsonRequest) {
                    _state = RequestState.Processing;
                    var data = Encoding.Default.GetString(_buffer, 0, bytes);

                    if (!Commands.Parse(data)) {
                        _logger.LogError("Failed to execute a MUS command. Raw data: {Data}", data);
                    }

                    Dispose();

                    return;
                }

                if (_request.Length + bytes > MaxRequestBytes) {
                    _state = RequestState.Processing;
                    Reject("RCON request exceeds the maximum size.");

                    return;
                }

                _request.Write(_buffer, 0, bytes);

                if (_request.GetBuffer().AsSpan(0, (int)_request.Length).IndexOf((byte)'\n') >= 0) {
                    ProcessJsonRequest();
                }
                else {
                    Receive();
                }
            }
        }
        catch (Exception exception) {
            _logger.LogWarning(exception, "Failed to read an RCON request.");

            lock (_gate) {
                if (_state != RequestState.Pending) {
                    return;
                }

                _state = RequestState.Processing;
                Reject("Malformed RCON request.");
            }
        }
    }

    private ICommandManager Commands => _commands ?? PlusEnvironment.RconSocket.GetCommands();

    private void ProcessJsonRequest()
    {
        _state = RequestState.Processing;
        _timeout.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        try {
            var bytes = _request.GetBuffer().AsSpan(0, (int)_request.Length);
            var newline = bytes.IndexOf((byte)'\n');

            if (newline >= 0) {
                if (!OnlyWhitespace(bytes[(newline + 1)..])) {
                    Reject("Only one RCON request is allowed per connection.");

                    return;
                }

                bytes = bytes[..newline];
            }

            var request = JsonSerializer.Deserialize<AcknowledgedRequest>(StrictUtf8.GetString(bytes), JsonOptions);

            if (!Valid(request)) {
                Reject("Invalid RCON request.");

                return;
            }

            var legacy = request!.Command + Convert.ToChar(1) + string.Join(':', request.Parameters!);

            if (!Commands.Parse(legacy)) {
                Reject($"RCON command '{request.Command}' was rejected.");

                return;
            }

            Respond(0, "OK");
        }
        catch (JsonException) {
            Reject("Malformed RCON JSON.");
        }
        catch (DecoderFallbackException) {
            Reject("RCON request is not valid UTF-8.");
        }
        catch (Exception exception) {
            _logger.LogWarning(exception, "Failed to execute an acknowledged RCON request.");
            Reject("RCON command failed.");
        }
    }

    private static bool Valid(AcknowledgedRequest? request)
    {
        if (request?.Command == null || request.Parameters == null || !AcknowledgedCommands.Contains(request.Command)) {
            return false;
        }

        if (request.Parameters.Length > 8) {
            return false;
        }

        return request.Parameters.All(parameter => parameter is { Length: <= 1024 }
            && parameter.IndexOfAny([':', Convert.ToChar(1), '\r', '\n', '\0']) < 0);
    }

    private static byte FirstNonWhitespace(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) {
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) {
                return value;
            }
        }

        return 0;
    }

    private static bool OnlyWhitespace(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) {
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) {
                return false;
            }
        }

        return true;
    }

    private void Reject(string message) => Respond(1, message);

    private void TimeoutRequest()
    {
        lock (_gate) {
            if (_state != RequestState.Pending) {
                return;
            }

            _state = RequestState.TimedOut;
            Reject("RCON request timed out.");
        }
    }

    private void Respond(int status, string message)
    {
        try {
            var socket = _socket;

            if (socket == null) {
                return;
            }

            var response = JsonSerializer.SerializeToUtf8Bytes(new { status, message });
            var framed = new byte[response.Length + 1];
            response.CopyTo(framed, 0);
            framed[^1] = (byte)'\n';
            var sent = 0;

            while (sent < framed.Length) {
                var written = socket.Send(framed, sent, framed.Length - sent, SocketFlags.None);

                if (written == 0) {
                    break;
                }

                sent += written;
            }
        }
        catch (SocketException) { }
        finally {
            Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate) {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            _state = RequestState.Disposed;
            _timeout.Dispose();
            var socket = Interlocked.Exchange(ref _socket, null);

            if (socket != null) {
                try {
                    socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException) { }

                socket.Dispose();
            }

            _request.Dispose();
        }
    }

    private sealed class AcknowledgedRequest
    {
        [JsonPropertyName("command")]
        public string? Command { get; init; }
        [JsonPropertyName("parameters")]
        public string[]? Parameters { get; init; }
    }

    private enum RequestState
    {
        Pending, Processing, TimedOut, Disposed
    }
}
