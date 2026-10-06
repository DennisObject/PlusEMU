using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class CheckGnomeNameEvent(IGnomePackageService packages) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var petName = packet.ReadString();
        packages.Open(room, session, itemId, petName);

        return Task.CompletedTask;
    }
}
