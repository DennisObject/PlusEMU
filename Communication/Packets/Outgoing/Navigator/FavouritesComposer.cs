using System.Collections;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class FavouritesComposer : IServerPacket
{
    private readonly int[] _favouriteIds;
    public uint MessageId => ServerPacketHeader.FavouritesComposer;

    public FavouritesComposer(ArrayList favouriteIds)
    {
        _favouriteIds = favouriteIds.Cast<int>().ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(50);
        packet.WriteInteger(_favouriteIds.Length);

        foreach (var id in _favouriteIds) {
            packet.WriteInteger(id);
        }
    }
}
