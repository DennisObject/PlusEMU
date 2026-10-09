using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.Communication.Packets.Outgoing.WiredVariables;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed class WiredUserVariablesRequestEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!WiredVariableWireProtocol.TryReadRequestVersion(packet, out var exact)) {
            return Task.CompletedTask;
        }

        if (exact) {
            WiredVariableWireProtocol.Enable(session);
        }

        menus.ShowSnapshot(room, session);

        return Task.CompletedTask;
    }
}
