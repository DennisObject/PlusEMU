using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public sealed class GetRoomFilterListComposer : IServerPacket
{
    private readonly ImmutableArray<string> _words;

    public uint MessageId => ServerPacketHeader.GetRoomFilterListComposer;

    public GetRoomFilterListComposer(IEnumerable<string> words) => _words = [.. words];

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_words.Length);

        foreach (var word in _words) {
            packet.WriteString(word);
        }
    }
}
