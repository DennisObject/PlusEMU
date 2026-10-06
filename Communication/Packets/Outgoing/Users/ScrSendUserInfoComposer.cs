using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Outgoing.Users;

public class ScrSendUserInfoComposer : IServerPacket
{
    public const int InfoResponse = 1, PurchaseResponse = 2, ExpiringResponse = 3;
    private readonly UserAccess.Snapshot _snapshot;
    private readonly DateTimeOffset _now;
    private readonly int _responseType;
    public uint MessageId => ServerPacketHeader.ScrSendUserInfoComposer;
    public ScrSendUserInfoComposer(UserAccess access, int responseType = InfoResponse)
    {
        _snapshot = access.Capture(out var now);
        _now = now;
        _responseType = responseType;
    }
    public void Compose(IOutgoingPacket packet)
    {
        var membership = _snapshot.Membership;
        var seconds = membership.SecondsLeft(_now);
        var days = (int)Math.Min(int.MaxValue, (seconds + ClubMembership.Day - 1) / ClubMembership.Day);
        var ahead = Math.Max(0, days - 1) / 31;
        packet.WriteString("habbo_club");
        packet.WriteInteger(days - ahead * 31);
        packet.WriteInteger((int)Math.Min(int.MaxValue, membership.Elapsed(_now) / ClubMembership.Period));
        packet.WriteInteger(ahead);
        packet.WriteInteger(_responseType);
        packet.WriteBoolean(membership.FirstStartedAt is not null);
        // HC branding in the purse is level 1; rights still carry level 2 for all merged benefits.
        packet.WriteBoolean(false);
        packet.WriteInteger((int)Math.Min(int.MaxValue, membership.Elapsed(_now) / ClubMembership.Day));
        packet.WriteInteger(0);
        packet.WriteInteger((int)Math.Min(int.MaxValue, seconds / 60));
        packet.WriteInteger((int)Math.Min(int.MaxValue, membership.SecondsSinceModified(_now) / 60));
    }
}
