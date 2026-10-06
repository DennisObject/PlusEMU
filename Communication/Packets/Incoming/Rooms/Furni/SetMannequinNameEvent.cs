using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class SetMannequinNameEvent(IRoomItemMetadataService metadata) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var name = packet.ReadString();
        metadata.SetMannequinName(session, new(itemId, name));

        return Task.CompletedTask;
    }
}
