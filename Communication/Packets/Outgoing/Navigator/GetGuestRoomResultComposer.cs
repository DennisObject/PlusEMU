using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class GetGuestRoomResultComposer : IServerPacket
{
    private readonly GameClient _session;
    private readonly RoomData _data;
    private readonly bool _isLoading;
    private readonly bool _checkEntry;
    public uint MessageId => ServerPacketHeader.GetGuestRoomResultComposer;

    public GetGuestRoomResultComposer(GameClient session, RoomData data, bool isLoading, bool checkEntry)
    {
        _session = session;
        _data = data;
        _isLoading = isLoading;
        _checkEntry = checkEntry;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_isLoading);
        packet.WriteUInteger(_data.Id);
        packet.WriteString(_data.Name);
        packet.WriteInteger(_data.OwnerId);
        packet.WriteString(_data.OwnerName);
        packet.WriteInteger(RoomAccessUtility.GetRoomAccessPacketNum(_data.Access));
        packet.WriteInteger(_data.UsersNow);
        packet.WriteInteger(_data.UsersMax);
        packet.WriteString(_data.Description);
        packet.WriteInteger(_data.TradeSettings);
        packet.WriteInteger(_data.Score);
        packet.WriteInteger(0); //Top rated room rank.
        packet.WriteInteger(_data.Category);
        packet.WriteInteger(_data.Tags.Count);
        foreach (var tag in _data.Tags) packet.WriteString(tag);
        if (_data.Group != null && _data.Promotion != null)
        {
            packet.WriteInteger(62);
            packet.WriteInteger(_data.Group?.Id ?? 0);
            packet.WriteString(_data.Group == null ? "" : _data.Group.Name);
            packet.WriteString(_data.Group == null ? "" : _data.Group.Badge);
            packet.WriteString(_data.Promotion != null ? _data.Promotion.Name : "");
            packet.WriteString(_data.Promotion != null ? _data.Promotion.Description : "");
            packet.WriteInteger(_data.Promotion?.MinutesLeft ?? 0);
        }
        else if (_data.Group != null && _data.Promotion == null)
        {
            packet.WriteInteger(58);
            packet.WriteInteger(_data.Group?.Id ?? 0);
            packet.WriteString(_data.Group == null ? "" : _data.Group.Name);
            packet.WriteString(_data.Group == null ? "" : _data.Group.Badge);
        }
        else if (_data.Group == null && _data.Promotion != null)
        {
            packet.WriteInteger(60);
            packet.WriteString(_data.Promotion != null ? _data.Promotion.Name : "");
            packet.WriteString(_data.Promotion != null ? _data.Promotion.Description : "");
            packet.WriteInteger(_data.Promotion?.MinutesLeft ?? 0);
        }
        else
            packet.WriteInteger(56);
        packet.WriteBoolean(_checkEntry);
        packet.WriteBoolean(false);
        packet.WriteBoolean(false);
        packet.WriteBoolean(false);
        packet.WriteInteger(_data.WhoCanMute);
        packet.WriteInteger(_data.WhoCanKick);
        packet.WriteInteger(_data.WhoCanBan);
        packet.WriteBoolean(_session.GetHabbo().Access.Can(PermissionKeys.ModerationTool) || _data.OwnerName == _session.GetHabbo().Username);
        packet.WriteInteger(_data.ChatMode);
        packet.WriteInteger(_data.ChatSize);
        packet.WriteInteger(_data.ChatSpeed);
        packet.WriteInteger(_data.ExtraFlood);
        packet.WriteInteger(_data.ChatDistance);

    }
}