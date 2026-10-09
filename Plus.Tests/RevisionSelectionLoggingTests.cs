using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Handshake;
using Plus.Communication.Revisions;
using Xunit;

namespace Plus.Tests;

public class RevisionSelectionLoggingTests
{
    [Fact]
    public async Task AcceptedRevisionIsLoggedOncePerConnectionWithoutHelloMetadata()
    {
        var cache = new RevisionsCache();
        cache.Revisions = new Dictionary<string, Revision> { [cache.InternalRevision.Name] = cache.InternalRevision };
        var logger = new RecordingLogger();
        var handler = new ClientHelloEvent(cache, logger);
        var server = CatalogSnapshotTestSupport.Proxy<IGameServer>((method, _) => throw new NotSupportedException(method));
        var first = new FlashGameClient(server, new FlashPacketFactory(), TestLogging.GameClient);
        var second = new FlashGameClient(server, new FlashPacketFactory(), TestLogging.GameClient);

        using (var hello = Hello(cache.InternalRevision.Name)) {
            await handler.Parse(first, new FlashIncomingPacket(hello));
        }

        using (var hello = Hello(cache.InternalRevision.Name)) {
            await handler.Parse(first, new FlashIncomingPacket(hello));
        }

        using (var hello = Hello(cache.InternalRevision.Name)) {
            await handler.Parse(second, new FlashIncomingPacket(hello));
        }

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal($"Packet revision selected {cache.InternalRevision.Name}.", entry.Message);
        });
        Assert.Same(cache.InternalRevision, first.Revision);
        Assert.Same(cache.InternalRevision, second.Revision);
    }

    [Fact]
    public async Task UnknownRevisionDoesNotProduceASelectionLog()
    {
        var logger = new RecordingLogger();
        var handler = new ClientHelloEvent(new RevisionsCache(), logger);
        var server = CatalogSnapshotTestSupport.Proxy<IGameServer>((method, _) => throw new NotSupportedException(method));
        var client = new FlashGameClient(server, new FlashPacketFactory(), TestLogging.GameClient);
        using var hello = Hello("unknown");

        await handler.Parse(client, new FlashIncomingPacket(hello));

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.True(client.Closed.IsCancellationRequested);
    }

    private static RecyclableMemoryStream Hello(string revision)
    {
        var stream = PlusMemoryStream.GetStream();
        var outgoing = new FlashOutgoingPacket(stream);
        outgoing.WriteString(revision);
        outgoing.WriteString("private-client-metadata");
        outgoing.WriteInt(2);
        outgoing.WriteInt(1);

        stream.Position = 6;

        return stream;
    }

    private sealed class RecordingLogger : ILogger<ClientHelloEvent>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
