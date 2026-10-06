using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class CfhTopicsInitComposer : IServerPacket
{
    private readonly ImmutableArray<CfhTopicCategorySnapshot> _userActionPresets;
    public uint MessageId => ServerPacketHeader.CfhTopicsInitComposer;

    public CfhTopicsInitComposer(ImmutableArray<CfhTopicCategorySnapshot> userActionPresets)
    {
        _userActionPresets = userActionPresets;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_userActionPresets.Length);

        foreach (var cat in _userActionPresets) {
            packet.WriteString(cat.Name);
            packet.WriteInteger(cat.Topics.Length);

            foreach (var preset in cat.Topics) {
                packet.WriteString(preset.Caption);
                packet.WriteInteger(preset.Id);
                packet.WriteString(preset.Type);
            }
        }
    }
}
