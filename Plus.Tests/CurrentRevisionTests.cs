using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Handshake;
using Plus.Communication.Revisions;
using Xunit;

namespace Plus.Tests;

public class CurrentRevisionTests
{
    [Fact]
    public async Task EmptyRevisionDirectoryStartsWithOnlyTheCompiledCurrentContract()
    {
        await InDirectory(async (cache, directory) =>
        {
            await cache.Start();

            Assert.Same(cache.InternalRevision, Assert.Single(cache.Revisions).Value);
            Assert.Equal("example.json", Path.GetFileName(Assert.Single(Directory.GetFiles(directory))));
            Assert.Equal(0u, cache.InternalRevision.IncomingIdToInternalIdMapping[ClientPacketHeader.InfoRetrieveEvent]);
            Assert.All(cache.InternalRevision.IncomingIdToInternalIdMapping, pair => Assert.Equal(pair.Key, pair.Value));
            Assert.All(cache.InternalRevision.InternalIdToOutgoingIdMapping, pair => Assert.Equal(pair.Key, pair.Value));
        });
    }

    [Theory]
    [InlineData("NITRO-1-6-6")]
    [InlineData("NITRO-3-6-0")]
    [InlineData("OCTANE-3-6-0-FLOOR-20260909")]
    [InlineData("OCTANE-AIR-IDS-WIN63-202609161723-93809945")]
    public async Task StaleHostFilesCannotEnableUnsupportedRevisions(string name)
    {
        await InDirectory(async (cache, directory) =>
        {
            var path = Path.Join(directory, "stale.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                Name = name,
                cache.InternalRevision.IncomingHeaders,
                cache.InternalRevision.OutgoingHeaders
            }));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(cache.Start);

            Assert.Contains(path, error.Message);
            Assert.Contains("unsupported packet revision", error.Message);
            Assert.Empty(cache.Revisions);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RenamingAnOldWireMapDoesNotMakeItSupported(bool incoming)
    {
        await InDirectory(async (cache, directory) =>
        {
            var requests = cache.InternalRevision.IncomingHeaders.ToDictionary();
            var responses = cache.InternalRevision.OutgoingHeaders.ToDictionary();
            var headers = incoming ? requests : responses;
            var key = headers.Keys.First();
            headers[key] = headers[key] == 65535 ? 65534u : 65535u;
            await File.WriteAllTextAsync(Path.Join(directory, "renamed.json"), JsonSerializer.Serialize(new
            {
                cache.InternalRevision.Name,
                IncomingHeaders = requests,
                OutgoingHeaders = responses
            }));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(cache.Start);

            Assert.Contains("headers do not match", error.Message);
            Assert.Empty(cache.Revisions);
        });
    }

    [Theory]
    [InlineData("NITRO-1-6-6")]
    [InlineData("NITRO-3-6-0")]
    [InlineData("OCTANE-3-6-0-FLOOR-20260909")]
    public async Task HelloRejectsLegacyNamesEvenIfTheyAppearInAnExternalCache(string name)
    {
        var cache = new RevisionsCache();
        cache.Revisions = new Dictionary<string, Revision> { [name] = cache.InternalRevision };
        var client = Client();
        var handler = new ClientHelloEvent(cache, NullLogger<ClientHelloEvent>.Instance);

        await handler.Parse(client, Hello(name));

        Assert.True(client.Closed.IsCancellationRequested);
        Assert.Null(client.Revision);
    }

    [Fact]
    public async Task CurrentHelloAlwaysSelectsTheCompiledContract()
    {
        var cache = new RevisionsCache();
        cache.Revisions = new Dictionary<string, Revision>
        {
            [cache.InternalRevision.Name] = new() { Name = cache.InternalRevision.Name }
        };
        var client = Client();

        await new ClientHelloEvent(cache, NullLogger<ClientHelloEvent>.Instance).Parse(client, Hello(cache.InternalRevision.Name));

        Assert.False(client.Closed.IsCancellationRequested);
        Assert.Same(cache.InternalRevision, client.Revision);
        Assert.Equal(0u, Assert.IsType<Revision>(client.Revision).IncomingIdToInternalIdMapping[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConcatenatedHelloDispatchesZeroOnlyForTheSupportedBuild(bool supported)
    {
        var cache = new RevisionsCache();
        var handler = new ClientHelloEvent(cache, NullLogger<ClientHelloEvent>.Instance);
        var received = new List<uint>();
        var server = CatalogSnapshotTestSupport.Proxy<IGameServer>((method, args) =>
        {
            if (method != "PacketReceived") {
                throw new NotSupportedException(method);
            }

            var id = Assert.IsType<uint>(args[1]);
            received.Add(id);

            return id == ClientPacketHeader.ClientHelloEvent
                ? handler.Parse(Assert.IsType<FlashGameClient>(args[0]), Assert.IsType<FlashIncomingPacket>(args[2]))
                : Task.CompletedTask;
        });
        var client = new FlashGameClient(server, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = cache.InternalRevision
        };
        using var stream = PlusMemoryStream.GetStream();
        var hello = new FlashOutgoingPacket(stream);
        hello.WriteString(supported ? cache.InternalRevision.Name : "NITRO-1-6-6");
        hello.WriteString("HTML5");
        hello.WriteInt(2);
        hello.WriteInt(1);
        var frame = stream.GetBuffer().AsMemory(0, (int)stream.Length);
        client.CreateHeader(frame, ClientPacketHeader.ClientHelloEvent);
        var frames = frame.ToArray().Concat(new byte[] { 0, 0, 0, 2, 0, 0 }).ToArray();

        client.OnReceived(frames, 0, frames.Length);

        Assert.Equal(!supported, client.Closed.IsCancellationRequested);
        Assert.Equal(supported ? new uint[] { 4000, 0 } : new uint[] { 4000 }, received);
        Assert.Same(cache.InternalRevision, client.Revision);
        Assert.Null(client._incompleteStream);

        if (!supported) {
            var subsequent = new byte[] { 0, 0, 0, 2, 0, 0 };
            client.OnReceived(subsequent, 0, subsequent.Length);
            Assert.Equal(new uint[] { 4000 }, received);
        }
    }

    [Fact]
    public void SelfFigureUpdateUsesTheNativeHeaderAndExactlyTwoOrderedStrings()
    {
        var composer = new Plus.Communication.Packets.Outgoing.Rooms.Avatar.AvatarAspectUpdateComposer("hd-180-1", "M");
        using var stream = PlusMemoryStream.GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));
        var incoming = new FlashIncomingPacket { Buffer = stream.ToArray()[6..] };

        Assert.Equal(1822u, composer.MessageId);
        Assert.Equal("hd-180-1", incoming.ReadString());
        Assert.Equal("M", incoming.ReadString());
        Assert.False(incoming.HasDataRemaining());
    }

    private static FlashGameClient Client() => new(
        CatalogSnapshotTestSupport.Proxy<IGameServer>((method, _) => throw new NotSupportedException(method)),
        new FlashPacketFactory(), TestLogging.GameClient);

    private static FlashIncomingPacket Hello(string name)
    {
        using var stream = PlusMemoryStream.GetStream();
        var packet = new FlashOutgoingPacket(stream);
        packet.WriteString(name);
        packet.WriteString("client");
        packet.WriteInt(2);
        packet.WriteInt(1);

        return new() { Buffer = stream.ToArray()[6..] };
    }

    private static async Task InDirectory(Func<RevisionsCache, string, Task> action)
    {
        var directory = Directory.CreateTempSubdirectory("current-revision-").FullName;
        var cache = new RevisionsCache();
        Assert.IsAssignableFrom<FieldInfo>(typeof(RevisionsCache).GetField("_directory", BindingFlags.NonPublic | BindingFlags.Instance))
            .SetValue(cache, directory);

        try {
            await action(cache, directory);
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}
