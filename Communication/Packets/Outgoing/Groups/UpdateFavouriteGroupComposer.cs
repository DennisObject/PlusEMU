using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class UpdateFavouriteGroupComposer : IServerPacket
{
    private readonly FavouriteGroupSnapshot _snapshot;
    public uint MessageId => ServerPacketHeader.UpdateFavouriteGroupComposer;

    public UpdateFavouriteGroupComposer(FavouriteGroupSnapshot snapshot) => _snapshot = snapshot;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_snapshot.VirtualId); //Sends 0 on .COM
        packet.WriteInteger(_snapshot.GroupId);
        packet.WriteInteger(3);
        packet.WriteString(_snapshot.Name);
    }
}