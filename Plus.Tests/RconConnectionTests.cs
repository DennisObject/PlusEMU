using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.RCON;
using Plus.Communication.RCON.Commands;
using Plus.HabboHotel.Users.Grants;
using Xunit;

namespace Plus.Tests;

public sealed class RconConnectionTests
{
    [Fact]
    public async Task AcknowledgesASplitJsonRequestOnlyAfterTheCommandSucceeds()
    {
        using var sockets = await ConnectedSockets.Create();
        var commands = new RecordingCommands(true);
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes("{\"command\":\"give_user_"));
        await Task.Delay(20);
        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes("currency\",\"parameters\":[\"42\",\"credits\",\"5\"]}\n"));

        var response = await ReadResponse(sockets.Client);
        Assert.Equal(0, response.GetProperty("status").GetInt32());
        Assert.Equal("give_user_currency\u000142:credits:5", commands.Request);
    }

    [Fact]
    public async Task ReportsACommandRejectionInsteadOfFabricatingSuccess()
    {
        using var sockets = await ConnectedSockets.Create();
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, new RecordingCommands(false));

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes("{\"command\":\"disconnect_user\",\"parameters\":[\"42\"]}\n"));

        var response = await ReadResponse(sockets.Client);
        Assert.Equal(1, response.GetProperty("status").GetInt32());
        Assert.Contains("rejected", response.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("{not-json}\n")]
    [InlineData("{\"command\":\"not_registered\",\"parameters\":[]}\n")]
    [InlineData("{\"command\":\"reload_filter\",\"parameters\":[],\"extra\":true}\n")]
    [InlineData("{\"command\":\"alert_user\",\"parameters\":[\"42\",\"unsafe:delimiter\"]}\n")]
    public async Task RejectsMalformedUnknownAndDelimiterInjectingRequests(string request)
    {
        using var sockets = await ConnectedSockets.Create();
        var commands = new RecordingCommands(true);
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes(request));

        Assert.Equal(1, (await ReadResponse(sockets.Client)).GetProperty("status").GetInt32());
        Assert.Null(commands.Request);
    }

    [Fact]
    public async Task RejectsRequestsBeyondTheBoundWithoutDispatching()
    {
        using var sockets = await ConnectedSockets.Create();
        var commands = new RecordingCommands(true);
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);
        var oversized = "{\"command\":\"alert_user\",\"parameters\":[\"" + new string('a', RconConnection.MaxRequestBytes) + "\"]}\n";

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes(oversized));

        Assert.Equal(1, (await ReadResponse(sockets.Client)).GetProperty("status").GetInt32());
        Assert.Null(commands.Request);
    }

    [Fact]
    public async Task TimesOutAnIncompleteFrameWithoutDispatching()
    {
        using var sockets = await ConnectedSockets.Create();
        var commands = new RecordingCommands(true);
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes("{\"command\":\"disconnect_user\""));

        var response = await ReadResponse(sockets.Client, TimeSpan.FromSeconds(3));
        Assert.Equal(1, response.GetProperty("status").GetInt32());
        Assert.Contains("timed out", response.GetProperty("message").GetString());
        Assert.Null(commands.Request);
    }

    [Fact]
    public async Task ACompletedFrameCannotTimeOutWhileItsCommandIsDispatching()
    {
        using var sockets = await ConnectedSockets.Create();
        var commands = new BlockingCommands();
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes("{\"command\":\"disconnect_user\",\"parameters\":[\"42\"]}\n"));
        await commands.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromSeconds(2.2));
        Assert.False(sockets.Client.Poll(0, SelectMode.SelectRead));

        commands.Release.SetResult();
        Assert.Equal(0, (await ReadResponse(sockets.Client)).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task PreservesTheLegacyOneReadProtocolWithoutAnAcknowledgement()
    {
        using var sockets = await ConnectedSockets.Create();
        var commands = new RecordingCommands(true);
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);

        await sockets.Client.SendAsync(Encoding.Default.GetBytes("disconnect_user\u000142"));
        var buffer = new byte[1];

        Assert.Equal(0, await sockets.Client.ReceiveAsync(buffer));
        Assert.Equal("disconnect_user\u000142", commands.Request);
    }

    private static async Task<JsonElement> ReadResponse(Socket socket, TimeSpan? timeout = null)
    {
        using var stream = new NetworkStream(socket, ownsSocket: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync().WaitAsync(timeout ?? TimeSpan.FromSeconds(2));

        return JsonDocument.Parse(line!).RootElement.Clone();
    }

    [Fact]
    public async Task AnAcknowledgedCommandGetsItsLargeParameterAndRepliesWithItsOutcomeAndResult()
    {
        using var sockets = await ConnectedSockets.Create();
        var payload = new string('A', 10_924);
        var commands = new AcknowledgedCommands(_ => Task.FromResult(GrantOutcome.Success(new { balance = 7 })));
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes("{\"command\":\"grant_user_bundle\",\"parameters\":[\"42\",\"order-1\",\"" + payload + "\"]}\n"));

        var response = await ReadResponse(sockets.Client);
        Assert.Equal(0, response.GetProperty("status").GetInt32());
        Assert.Equal("ok", response.GetProperty("message").GetString());
        Assert.Equal(7, response.GetProperty("result").GetProperty("balance").GetInt32());
        Assert.False(response.TryGetProperty("detail", out _));
        Assert.Equal(["42", "order-1", payload], commands.Received!);
    }

    [Theory]
    [InlineData(0, "user_online")]
    [InlineData(1, "busy")]
    [InlineData(2, "internal_error")]
    public async Task AnAcknowledgedRefusalOrFailureCarriesItsStableIdAndNoResult(int failure, string expected)
    {
        using var sockets = await ConnectedSockets.Create();
        var commands = new AcknowledgedCommands(_ => failure switch
        {
            0 => Task.FromResult(GrantOutcome.Fail(GrantOutcome.UserOnline)),
            1 => throw new TimeoutException(),
            _ => throw new InvalidOperationException(),
        });
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes("{\"command\":\"take_user_badge\",\"parameters\":[\"42\",\"ACH\"]}\n"));

        var response = await ReadResponse(sockets.Client);
        Assert.Equal(1, response.GetProperty("status").GetInt32());
        Assert.Equal(expected, response.GetProperty("message").GetString());
        Assert.False(response.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task ARequestBeyondTheCapIsRejectedAsTooLarge()
    {
        using var sockets = await ConnectedSockets.Create();
        var commands = new AcknowledgedCommands(_ => throw new InvalidOperationException("dispatched"));
        using var connection = new RconConnection(sockets.Server, NullLogger<RconConnection>.Instance, commands);
        var oversized = "{\"command\":\"grant_user_bundle\",\"parameters\":[\"42\",\"k\",\"" + new string('A', RconConnection.MaxRequestBytes) + "\"]}\n";

        await sockets.Client.SendAsync(Encoding.UTF8.GetBytes(oversized));

        Assert.Equal("too_large", (await ReadResponse(sockets.Client)).GetProperty("message").GetString());
        Assert.Null(commands.Received);
    }

    private sealed class AcknowledgedCommands(Func<string[], Task<GrantOutcome>> execute) : ICommandManager, IAcknowledgedRconCommand
    {
        public string[]? Received { get; private set; }
        public string Key => "acknowledged";
        string IRconCommand.Parameters => string.Empty;
        public string Description => string.Empty;

        public bool Parse(string data) => throw new InvalidOperationException("Acknowledged commands are not parsed.");
        public IAcknowledgedRconCommand Acknowledged(string command) => this;

        public Task<GrantOutcome> Execute(string[] parameters)
        {
            Received = parameters;

            return execute(parameters);
        }
    }

    private sealed class RecordingCommands(bool result) : ICommandManager
    {
        public string? Request { get; private set; }

        public bool Parse(string data)
        {
            Request = data;

            return result;
        }
    }

    private sealed class BlockingCommands : ICommandManager
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Parse(string data)
        {
            Entered.SetResult();
            Release.Task.GetAwaiter().GetResult();

            return true;
        }
    }

    private sealed class ConnectedSockets(Socket client, Socket server) : IDisposable
    {
        public Socket Client { get; } = client;
        public Socket Server { get; } = server;

        public static async Task<ConnectedSockets> Create()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var connect = client.ConnectAsync(listener.LocalEndPoint!);
            var server = await listener.AcceptAsync();
            await connect;

            return new ConnectedSockets(client, server);
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }
}
