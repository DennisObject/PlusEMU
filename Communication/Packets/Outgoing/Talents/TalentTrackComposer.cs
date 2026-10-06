using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Talents;

namespace Plus.Communication.Packets.Outgoing.Talents;

public class TalentTrackComposer(string type, ImmutableArray<TalentTrackLevelSnapshot> levels) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.TalentTrackComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(type);
        packet.WriteInteger(levels.Length);

        foreach (var level in levels)
        {
            packet.WriteInteger(level.Level); //First level
            packet.WriteInteger(0); //Progress, 0 = nothing, 1 = started, 2 = done
            packet.WriteInteger(level.SubLevels.Length);

            foreach (var sub in level.SubLevels)
            {
                packet.WriteInteger(0); //Achievement Id
                packet.WriteInteger(0); //Achievement level
                packet.WriteString(sub.Badge); //Achievement name
                packet.WriteInteger(0); //Progress, 0 = nothing, 1 = started, 2 = done
                packet.WriteInteger(0); //My actual progress
                packet.WriteInteger(sub.RequiredProgress);
            }

            packet.WriteInteger(level.Actions.Length);

            foreach (var action in level.Actions)
            {
                packet.WriteString(action);
            }

            packet.WriteInteger(level.Gifts.Length);

            foreach (var gift in level.Gifts)
            {
                packet.WriteString(gift);
                packet.WriteInteger(0);
            }
        }
    }
}
