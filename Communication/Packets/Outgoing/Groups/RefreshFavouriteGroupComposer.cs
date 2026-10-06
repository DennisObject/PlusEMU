using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class RefreshFavouriteGroupComposer : IServerPacket
{
    private readonly int _id;
    public uint MessageId => ServerPacketHeader.RefreshFavouriteGroupComposer;

    public RefreshFavouriteGroupComposer(int id)
    {
        _id = id;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(_id);
}
