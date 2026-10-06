using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Outgoing.Habbicons;

public sealed class UserHabbiconsComposer(HabbiconSnapshot snapshot) : IServerPacket
{
    private readonly ImmutableArray<HabbiconItem> _items = snapshot.Items.Values.Where(item => item.Collected).ToImmutableArray();
    private readonly ImmutableArray<int> _recent = snapshot.Recent.ToImmutableArray();
    public uint MessageId => ServerPacketHeader.UserHabbiconsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_items.Length);

        foreach (var item in _items) {
            packet.WriteInteger(item.Id);
            packet.WriteInteger(item.State);
        }

        packet.WriteInteger(_recent.Length);

        foreach (var id in _recent) {
            packet.WriteInteger(id);
        }
    }
}
