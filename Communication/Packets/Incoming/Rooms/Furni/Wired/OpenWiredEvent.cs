using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

/// <summary>WIRED_OPEN carries only an item id; it is not the two-field use-furniture packet.</summary>
internal sealed class OpenWiredEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, false, true))
            return Task.CompletedTask;
        uint id;
        try
        {
            id = packet.ReadUInt();
        }
        catch (ArgumentException)
        {
            return Task.CompletedTask;
        }
        if (packet.HasDataRemaining())
            return Task.CompletedTask;
        var item = room.GetRoomItemHandler().GetItem(id);
        if (item is { IsWired: true, IsTemporary: false })
            item.Interactor.OnTrigger(session, item, 0, true);
        return Task.CompletedTask;
    }
}
