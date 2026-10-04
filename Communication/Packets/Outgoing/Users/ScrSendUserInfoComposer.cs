using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Outgoing.Users;

// Club status stays free for everyone; the day counters show time bought on the club page.
public class ScrSendUserInfoComposer : IServerPacket
{
    public const int InfoResponse = 1;
    public const int PurchaseResponse = 2;

    private readonly UserAccess _access;
    private readonly int _secondsLeft;
    private readonly int _responseType;
    public uint MessageId => ServerPacketHeader.ScrSendUserInfoComposer;

    public ScrSendUserInfoComposer(UserAccess access, int secondsLeft = 0, int responseType = InfoResponse)
    {
        _access = access;
        _secondsLeft = Math.Max(0, secondsLeft);
        _responseType = responseType;
    }

    public void Compose(IOutgoingPacket packet)
    {
        var daysLeft = (int)Math.Ceiling(_secondsLeft / 86400.0);
        // The current period holds 1-31 days, so a fresh month reads as 31 days rather than 0.
        var periodsAhead = Math.Max(0, daysLeft - 1) / 31;
        packet.WriteString("habbo_club");
        packet.WriteInteger(daysLeft - periodsAhead * 31); //display days
        var level = ClubAccess.LevelFor(_access);
        packet.WriteInteger(level);
        packet.WriteInteger(periodsAhead); //display months
        packet.WriteInteger(_responseType);
        packet.WriteBoolean(level > 0); // hc
        packet.WriteBoolean(level > 1); // vip
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(_secondsLeft > 0 ? _secondsLeft / 60 : 495);
    }
}
