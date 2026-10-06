using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public class HousekeepingUserDetailComposer : IServerPacket
{
    private readonly HousekeepingUserDetail? _user;
    public uint MessageId => ServerPacketHeader.HousekeepingUserDetailComposer;

    public HousekeepingUserDetailComposer(HousekeepingUserDetail? user) => _user = user;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_user != null);

        if (_user == null) {
            return;
        }

        packet.WriteInteger(_user.Id);
        packet.WriteString(_user.Username);
        packet.WriteString(_user.Motto);
        packet.WriteString(_user.Figure);
        packet.WriteInteger(_user.Rank);
        packet.WriteString(_user.RankName);
        packet.WriteBoolean(_user.Online);
        packet.WriteInteger(_user.LastOnlineAt);
        packet.WriteInteger(_user.Credits);
        packet.WriteInteger(_user.Duckets);
        packet.WriteInteger(_user.Diamonds);
        packet.WriteString(_user.Email);
        packet.WriteString(_user.IpLast);
        packet.WriteBoolean(_user.IsBanned);
        packet.WriteBoolean(_user.IsMuted);
        packet.WriteBoolean(_user.IsTradeLocked);
    }
}
