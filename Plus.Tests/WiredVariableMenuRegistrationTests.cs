using Plus.HabboHotel.Items.Wired.Variables;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Xunit;

namespace Plus.Tests;

public class WiredVariableMenuRegistrationTests
{
    [Theory]
    [InlineData("1.6.6.json")]
    [InlineData("example.json")]
    public void ConcreteMenuHandlersHaveRealDispatchAndCollisionFreeProfileMappings(string profile)
    {
        IPacketEvent[] handlers = [new WiredUserVariablesRequestEvent(new WiredVariableMenuService()), new WiredUserVariableUpdateEvent(new WiredVariableMenuService()), new WiredUserVariableManageEvent(new WiredVariableMenuService()),
            new WiredAllVariablesRequestEvent(new WiredVariableMenuService()), new WiredVariableHashesEvent(new WiredVariableMenuService()),
            new WiredVariableHoldersRequestEvent(new WiredVariableMenuService()), new WiredVariableHoldersPageEvent(new WiredVariableMenuService())];
        using var manager = new PacketManager(handlers, NullLogger<PacketManager>.Instance);
        var registered = (Dictionary<uint, IPacketEvent>)typeof(PacketManager).GetField("_incomingPackets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        Assert.Equal(7, registered.Count);
        var revision = JsonSerializer.Deserialize<Revision>(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "revisions", profile)))!;

        foreach (var handler in handlers) {
            var name = handler.GetType().Name;
            var id = (uint)typeof(ClientPacketHeader).GetField(name)!.GetRawConstantValue()!;
            Assert.Same(handler, registered[id]);
            Assert.Equal(id, revision.IncomingHeaders[name]);
            Assert.Single(revision.IncomingHeaders, pair => pair.Value == id);
        }

        foreach (var name in new[] { nameof(ServerPacketHeader.WiredUserVariablesDataComposer), nameof(ServerPacketHeader.WiredAllVariablesHashComposer),
            nameof(ServerPacketHeader.WiredAllVariablesDiffComposer), nameof(ServerPacketHeader.WiredVariableHoldersComposer),
            nameof(ServerPacketHeader.WiredVariableHoldersPageComposer) }) {
            var id = (uint)typeof(ServerPacketHeader).GetField(name)!.GetRawConstantValue()!;
            Assert.Equal(id, revision.OutgoingHeaders[name]);
            Assert.Single(revision.OutgoingHeaders, pair => pair.Value == id);
        }
    }
}
