using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal sealed class UpdateGroupBadgeEvent(IGroupAppearanceService appearance) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var count = packet.ReadInt();

        if (count < 3 || count > 15 || count % 3 != 0 || packet.Buffer.Length != count * sizeof(int))
        {
            return Task.CompletedTask;
        }

        var parts = ImmutableArray.CreateBuilder<GroupBadgePartRequest>(count / 3);

        for (var i = 0; i < count / 3; i++)
        {
            parts.Add(new(packet.ReadInt(), packet.ReadInt(), packet.ReadInt()));
        }

        return appearance.UpdateBadge(session, groupId, parts.MoveToImmutable());
    }
}
