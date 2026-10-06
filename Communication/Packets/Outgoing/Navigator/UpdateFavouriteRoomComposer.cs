using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class UpdateFavouriteRoomComposer : IServerPacket
{
    private readonly uint _roomId;
    private readonly bool _added;

    public uint MessageId => ServerPacketHeader.UpdateFavouriteRoomComposer;

    public UpdateFavouriteRoomComposer(uint roomId, bool added)
    {
        _roomId = roomId;
        _added = added;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
        packet.WriteBoolean(_added);
    }
}
