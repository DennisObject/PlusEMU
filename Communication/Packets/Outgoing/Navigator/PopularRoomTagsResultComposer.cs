using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class PopularRoomTagsResultComposer : IServerPacket
{
    private readonly ICollection<KeyValuePair<string, int>> _tags;

    public uint MessageId => ServerPacketHeader.PopularRoomTagsResultComposer;

    public PopularRoomTagsResultComposer(ICollection<KeyValuePair<string, int>> tags)
    {
        _tags = tags.ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_tags.Count);

        foreach (var tag in _tags) {
            packet.WriteString(tag.Key);
            packet.WriteInteger(tag.Value);
        }
    }
}
