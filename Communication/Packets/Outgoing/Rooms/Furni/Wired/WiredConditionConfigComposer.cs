using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public class WiredConditionConfigComposer : IServerPacket
{
    private readonly IWiredItem _box;

    public uint MessageId => ServerPacketHeader.WiredConditionConfigComposer;

    public WiredConditionConfigComposer(IWiredItem box)
    {
        _box = box;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(false);
        packet.WriteInteger(5);
        packet.WriteInteger(_box.SetItems.Count);
        foreach (var item in _box.SetItems.Values.ToList()) packet.WriteUInteger(item.Id);
        packet.WriteInteger(_box.Item.Definition.SpriteId);
        packet.WriteUInteger(_box.Item.Id);
        packet.WriteString(_box.StringData);
        if (_box.Type == WiredBoxType.ConditionMatchStateAndPosition || _box.Type == WiredBoxType.ConditionDontMatchStateAndPosition)
        {
            if (string.IsNullOrEmpty(_box.StringData))
                _box.StringData = "0;0;0";
            packet.WriteInteger(3); //Loop
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[0]) : 0);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[1]) : 0);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[2]) : 0);
        }
        else if (_box.Type == WiredBoxType.ConditionUserCountInRoom || _box.Type == WiredBoxType.ConditionUserCountDoesntInRoom)
        {
            if (string.IsNullOrEmpty(_box.StringData))
                _box.StringData = "0;0";
            packet.WriteInteger(2); //Loop
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[0]) : 1);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[1]) : 50);
        }
        if (_box.Type == WiredBoxType.ConditionFurniHasNoFurni)
            packet.WriteInteger(1);
        if (_box.Type != WiredBoxType.ConditionUserCountInRoom && _box.Type != WiredBoxType.ConditionUserCountDoesntInRoom && _box.Type != WiredBoxType.ConditionFurniHasNoFurni
            && _box.Type != WiredBoxType.ConditionMatchStateAndPosition && _box.Type != WiredBoxType.ConditionDontMatchStateAndPosition)
            packet.WriteInteger(0);
        else if (_box.Type == WiredBoxType.ConditionFurniHasNoFurni)
        {
            if (string.IsNullOrEmpty(_box.StringData))
                _box.StringData = "0";
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[0]) : 50);
        }
        packet.WriteInteger(0);
        packet.WriteInteger(WiredBoxTypeUtility.GetWiredId(_box.Type));

    }
}