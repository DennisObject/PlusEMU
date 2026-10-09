using Plus.HabboHotel.Items.AreaHide;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni;

// Octane's existing nine-field adapter includes wallItems/invisibility after native AreaHideData.
public sealed class AreaHideComposer(uint itemId, AreaHideValues state, bool removed = false) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.AreaHideComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInt(itemId);
        packet.WriteBool(!removed && state.On);
        packet.WriteInt(state[1]);
        packet.WriteInt(state[2]);
        packet.WriteInt(state[3]);
        packet.WriteInt(state[4]);
        packet.WriteBool(state[7] == 1);
        packet.WriteBool(state[6] == 1);
        packet.WriteBool(state[5] == 1);
    }
}
