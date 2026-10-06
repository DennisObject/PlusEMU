using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed class WiredUserVariablesRequestEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (packet.HasDataRemaining()) {
            return Task.CompletedTask;
        }

        menus.ShowSnapshot(room, session);

        return Task.CompletedTask;
    }
}
