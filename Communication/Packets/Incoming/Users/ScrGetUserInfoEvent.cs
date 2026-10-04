using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Users;

internal class ScrGetUserInfoEvent : IPacketEvent
{
    private readonly IClubMembershipService _clubMemberships;

    public ScrGetUserInfoEvent(IClubMembershipService clubMemberships) => _clubMemberships = clubMemberships;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var secondsLeft = _clubMemberships.GetExpiry(session.GetHabbo().Id) - (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Send(new ScrSendUserInfoComposer(session.GetHabbo().Access, secondsLeft));
        return Task.CompletedTask;
    }
}
