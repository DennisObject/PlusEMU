using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class ManageGroupComposer : IServerPacket
{
    private readonly Group _group;
    private readonly string[] _badgeParts;

    public uint MessageId => ServerPacketHeader.ManageGroupComposer;

    public ManageGroupComposer(Group group, string[] badgeParts)
    {
        _group = group;
        _badgeParts = badgeParts;
    }

    public void Compose(IOutgoingPacket packet)
    {
        var room = _group.GetRoom();
        if (_group.RoomId != 0)
        {
            packet.WriteInteger(1);
            packet.WriteInteger((int)_group.RoomId);
            packet.WriteString(room?.Name ?? "");
            packet.WriteBoolean(false);
        }
        else
            packet.WriteInteger(0);
        packet.WriteBoolean(true);
        packet.WriteInteger(_group.Id);
        packet.WriteString(_group.Name);
        packet.WriteString(_group.Description);
        packet.WriteInteger((int)_group.RoomId);
        packet.WriteInteger(_group.Colour1);
        packet.WriteInteger(_group.Colour2);
        packet.WriteInteger(_group.Type == GroupType.Open ? 0 : _group.Type == GroupType.Locked ? 1 : 2);
        packet.WriteInteger(_group.AdminOnlyDeco);
        packet.WriteBoolean(false);
        packet.WriteString("");
        packet.WriteInteger(5);
        for (var x = 0; x < _badgeParts.Length; x++)
        {
            var symbol = _badgeParts[x];
            packet.WriteInteger(symbol.Length >= 6 ? int.Parse(symbol.Substring(0, 3)) : int.Parse(symbol.Substring(0, 2)));
            packet.WriteInteger(symbol.Length >= 6 ? int.Parse(symbol.Substring(3, 2)) : int.Parse(symbol.Substring(2, 2)));
            packet.WriteInteger(symbol.Length < 5 ? 0 : symbol.Length >= 6 ? int.Parse(symbol.Substring(5, 1)) : int.Parse(symbol.Substring(4, 1)));
        }
        var i = 0;
        while (i < 5 - _badgeParts.Length)
        {
            packet.WriteInteger(0);
            packet.WriteInteger(0);
            packet.WriteInteger(0);
            i++;
        }
        packet.WriteString(_group.Badge);
        packet.WriteInteger(_group.MemberCount);
        packet.WriteBoolean(_group.ForumEnabled);
    }
}