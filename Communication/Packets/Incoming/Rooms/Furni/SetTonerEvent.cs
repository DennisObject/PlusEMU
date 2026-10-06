using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class SetTonerEvent(IRoomItemMetadataService metadata) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var request = new TonerSettingsRequest((uint)packet.ReadInt(), packet.ReadInt(), packet.ReadInt(), packet.ReadInt());
        metadata.SetToner(room, session, request);

        return Task.CompletedTask;
    }
}
