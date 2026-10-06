using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public sealed class CreatableRoomModelsComposer(ImmutableArray<CreatableRoomModelSnapshot> models) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CreatableRoomModelsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(models.Length);

        foreach (var model in models)
        {
            packet.WriteString(model.Id);
            packet.WriteInteger(model.TileSize);
            packet.WriteInteger(model.MapSizeX);
            packet.WriteInteger(model.MapSizeY);
            packet.WriteInteger(model.RequiredClubLevel);
        }
    }
}
