using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal sealed class PurchaseGroupEvent(IGroupPurchaseService purchases) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var name = packet.ReadString();
        var description = packet.ReadString();
        var roomId = packet.ReadUInt();
        var mainColour = packet.ReadInt();
        var secondaryColour = packet.ReadInt();
        var valueCount = packet.ReadInt();
        if (valueCount is < 3 or > 15 || valueCount % 3 != 0 ||
            packet.Buffer.Length != valueCount * sizeof(int))
            return Task.CompletedTask;

        var parts = ImmutableArray.CreateBuilder<GroupPurchaseBadgePart>(valueCount / 3);
        for (var index = 0; index < valueCount / 3; index++)
            parts.Add(new(packet.ReadInt(), packet.ReadInt(), packet.ReadInt()));

        return purchases.Purchase(session, new(
            name, description, roomId, mainColour, secondaryColour, parts.MoveToImmutable()));
    }
}
