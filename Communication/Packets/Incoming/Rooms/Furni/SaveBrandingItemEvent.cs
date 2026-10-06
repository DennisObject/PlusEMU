using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class SaveBrandingItemEvent(IRoomItemMetadataService metadata) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        // The map is optional: an id-only frame keeps the placement-only behaviour of non-background furniture.
        ImmutableArray<string>? values = null;

        if (packet.HasDataRemaining()) {
            var count = packet.ReadInt();

            if (count < 0 || count > 128 || count % 2 != 0) {
                return Task.CompletedTask;
            }

            var pairs = new string[count];

            for (var index = 0; index < count; index++) {
                pairs[index] = packet.ReadString();
            }

            if (packet.HasDataRemaining()) {
                return Task.CompletedTask;
            }

            values = ImmutableArray.Create(pairs);
        }

        metadata.SetBranding(session, new(itemId, values));

        return Task.CompletedTask;
    }
}
