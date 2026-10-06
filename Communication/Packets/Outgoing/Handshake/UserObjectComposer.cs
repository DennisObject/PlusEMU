using Plus.HabboHotel.Users;
using System.Globalization;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class UserObjectComposer : IServerPacket
{
    private readonly UserObjectSnapshot _user;
    public uint MessageId => ServerPacketHeader.UserObjectComposer;

    public UserObjectComposer(UserObjectSnapshot user)
    {
        _user = user;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_user.Id);
        packet.WriteString(_user.Username);
        packet.WriteString(_user.Look);
        packet.WriteString(_user.Gender);
        packet.WriteString(_user.Motto);
        packet.WriteString("");
        packet.WriteBoolean(false);
        packet.WriteInteger(_user.Respect);
        packet.WriteInteger(_user.DailyRespectPoints);
        packet.WriteInteger(_user.DailyPetRespectPoints);
        packet.WriteBoolean(false); // Friends stream active
        packet.WriteString((_user.LastOnlineAt?.ToUnixTimeSeconds() ?? 0).ToString(CultureInfo.InvariantCulture)); // last online?
        packet.WriteBoolean(_user.ChangingName); // Can change name
        packet.WriteBoolean(false);
    }
}
