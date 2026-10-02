using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public class WiredEffectConfigComposer : IServerPacket
{
    private readonly IWiredItem _box;
    private readonly List<int> _blockedItems;

    public uint MessageId => ServerPacketHeader.WiredEffectConfigComposer;

    public WiredEffectConfigComposer(IWiredItem box, List<int> blockedItems)
    {
        _box = box;
        _blockedItems = blockedItems;
    }

    public void Compose(IOutgoingPacket packet)
    {
        if (WiredLegacyCustomEditor.IsCustom(_box))
        {
            if (!WiredLegacyCustomEditor.TryGetConfiguration(_box, out var descriptor, out var configuration))
                throw new InvalidDataException("Invalid stored custom Wired settings.");
            WiredLegacyProtocol.Write(packet, _box.Item.Id, _box.Item.Definition.SpriteId, descriptor, configuration, 0, []);
            return;
        }
        packet.WriteBoolean(false);
        packet.WriteInteger(15);
        packet.WriteInteger(_box.SetItems.Count);
        foreach (var item in _box.SetItems.Values.ToList()) packet.WriteUInteger(item.Id);
        packet.WriteInteger(_box.Item.Definition.SpriteId);
        packet.WriteUInteger(_box.Item.Id);
        if (_box.Type == WiredBoxType.EffectBotGivesHanditemBox)
        {
            if (string.IsNullOrEmpty(_box.StringData))
                _box.StringData = "Bot name;0";
            packet.WriteString(_box.StringData != null ? _box.StringData.Split(';')[0] : "");
        }
        else if (_box.Type == WiredBoxType.EffectBotFollowsUserBox)
        {
            if (string.IsNullOrEmpty(_box.StringData))
                _box.StringData = "0;Bot name";
            packet.WriteString(_box.StringData != null ? _box.StringData.Split(';')[1] : "");
        }
        else
            packet.WriteString(_box.StringData);
        if (_box.Type != WiredBoxType.EffectMatchPosition && _box.Type != WiredBoxType.EffectMoveAndRotate && _box.Type != WiredBoxType.EffectMuteTriggerer &&
            _box.Type != WiredBoxType.EffectBotFollowsUserBox && _box.Type != WiredBoxType.EffectBotGivesHanditemBox)
            packet.WriteInteger(0); // Loop
        else if (_box.Type == WiredBoxType.EffectMatchPosition)
        {
            if (string.IsNullOrEmpty(_box.StringData))
                _box.StringData = "0;0;0";
            packet.WriteInteger(3);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[0]) : 0);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[1]) : 0);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[2]) : 0);
        }
        else if (_box.Type == WiredBoxType.EffectMoveAndRotate)
        {
            if (string.IsNullOrEmpty(_box.StringData))
                _box.StringData = "0;0";
            packet.WriteInteger(2);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[0]) : 0);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[1]) : 0);
        }
        else if (_box.Type == WiredBoxType.EffectMuteTriggerer)
        {
            if (string.IsNullOrEmpty(_box.StringData))
                _box.StringData = "0;Message";
            packet.WriteInteger(1); //Count, for the time.
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[0]) : 0);
        }
        else if (_box.Type == WiredBoxType.EffectBotFollowsUserBox)
        {
            packet.WriteInteger(1); //Count, for the time.
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[0]) : 0);
        }
        else if (_box.Type == WiredBoxType.EffectBotGivesHanditemBox)
        {
            packet.WriteInteger(1);
            packet.WriteInteger(_box.StringData != null ? int.Parse(_box.StringData.Split(';')[1]) : 0);
        }
        if (_box is IWiredCycle && _box.Type != WiredBoxType.EffectKickUser && _box.Type != WiredBoxType.EffectMatchPosition && _box.Type != WiredBoxType.EffectMoveAndRotate &&
            _box.Type != WiredBoxType.EffectSetRollerSpeed)
        {
            var cycle = (IWiredCycle)_box;
            packet.WriteInteger(0);
            packet.WriteInteger(WiredBoxTypeUtility.GetWiredId(_box.Type));
            packet.WriteInteger(cycle.Delay);
        }
        else if (_box.Type == WiredBoxType.EffectMatchPosition || _box.Type == WiredBoxType.EffectMoveAndRotate)
        {
            var cycle = (IWiredCycle)_box;
            packet.WriteInteger(0);
            packet.WriteInteger(WiredBoxTypeUtility.GetWiredId(_box.Type));
            packet.WriteInteger(cycle.Delay);
        }
        else
        {
            packet.WriteInteger(0);
            packet.WriteInteger(WiredBoxTypeUtility.GetWiredId(_box.Type));
            packet.WriteInteger(0);
        }
        packet.WriteInteger(_blockedItems.Count); // Incompatable items loop
        foreach (var itemId in _blockedItems.ToList())
            packet.WriteInteger(itemId);

    }
}
