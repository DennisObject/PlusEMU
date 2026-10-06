using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal sealed class UpdateGroupIdentityEvent(IGroupAppearanceService appearance) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => appearance.UpdateIdentity(session,
        new(packet.ReadInt(), packet.ReadString(), packet.ReadString()));
}
