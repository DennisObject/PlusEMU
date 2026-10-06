using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Clothing;

namespace Plus.Communication.Packets.Outgoing.Avatar;

public class WardrobeComposer : IServerPacket
{
    private readonly ImmutableArray<WardrobeSlot> _slots;

    public WardrobeComposer(WardrobeSnapshot wardrobe)
    {
        _slots = wardrobe.Slots.Select(slot => new WardrobeSlot(slot.SlotId, slot.Look, slot.Gender.ToUpper())).ToImmutableArray();
    }

    public uint MessageId => ServerPacketHeader.WardrobeComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(_slots.Length);
        foreach (var slot in _slots)
        {
            packet.WriteInteger(slot.SlotId);
            packet.WriteString(slot.Look);
            packet.WriteString(slot.Gender);
        }
    }
}
