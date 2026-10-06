using Plus.HabboHotel.GameClients;
using Plus.Utilities;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class SlideObjectBundleComposer : IServerPacket
{
    private readonly int _fromX;
    private readonly int _fromY;
    private readonly double _fromZ;
    private readonly int _toX;
    private readonly int _toY;
    private readonly double _toZ;
    private readonly uint _rollerId;
    private readonly int _avatarId;
    private readonly uint _itemId;

    public uint MessageId => ServerPacketHeader.SlideObjectBundleComposer;

    public SlideObjectBundleComposer(int fromX, int fromY, double fromZ, int toX, int toY, double toZ, uint rollerId, int avatarId, uint itemId)
    {
        _fromX = fromX;
        _fromY = fromY;
        _fromZ = fromZ;
        _toX = toX;
        _toY = toY;
        _toZ = toZ;
        _rollerId = rollerId;
        _avatarId = avatarId;
        _itemId = itemId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        var isItem = _itemId > 0;
        packet.WriteInteger(_fromX);
        packet.WriteInteger(_fromY);
        packet.WriteInteger(_toX);
        packet.WriteInteger(_toY);
        packet.WriteInteger(isItem ? 1 : 0);

        if (isItem) {
            packet.WriteUInteger(_itemId);
        }
        else {
            packet.WriteUInteger(_rollerId);
            packet.WriteInteger(2);
            packet.WriteInteger(_avatarId);
        }

        packet.WriteString(TextHandling.GetString(_fromZ));
        packet.WriteString(TextHandling.GetString(_toZ));

        if (isItem) {
            packet.WriteUInteger(_rollerId);
        }
    }
}
