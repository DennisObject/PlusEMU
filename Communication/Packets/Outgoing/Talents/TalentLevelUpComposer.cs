using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Talents;

namespace Plus.Communication.Packets.Outgoing.Talents;

public sealed class TalentLevelUpComposer(string type, TalentTrackLevelSnapshot level) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.TalentLevelUpComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(type);
        packet.WriteInteger(level.Level);
        packet.WriteInteger(level.Actions.Length);

        foreach (var action in level.Actions) {
            packet.WriteString(action);
        }

        packet.WriteInteger(level.Gifts.Length);

        foreach (var gift in level.Gifts) {
            packet.WriteString(gift);
            packet.WriteInteger(0); // Product VIP days, not a furniture sprite ID.
        }
    }
}
