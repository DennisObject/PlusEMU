using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed class WiredUserVariablesRequestEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.GetWired().Settings.CanInspect(session) || packet.HasDataRemaining()) return Task.CompletedTask;
        session.Send(new WiredUserVariablesDataComposer(menus.Snapshot(room)));
        return Task.CompletedTask;
    }
}
