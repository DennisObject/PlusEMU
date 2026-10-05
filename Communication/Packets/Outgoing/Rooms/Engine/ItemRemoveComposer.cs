using System.Globalization;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ItemRemoveComposer : IServerPacket
{
    private readonly string _itemId;
    private readonly int _userId;

    public uint MessageId => ServerPacketHeader.ItemRemoveComposer;

    public ItemRemoveComposer(uint itemId, int userId)
    {
        _itemId = itemId.ToString(CultureInfo.InvariantCulture);
        _userId = userId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_itemId);
        packet.WriteBoolean(false);
        packet.WriteInteger(_userId);
    }
}