using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Bots;

namespace Plus.Communication.Packets.Outgoing.Inventory.Bots;

public sealed class BotInventoryComposer(ImmutableArray<BotInventorySnapshot> bots) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.BotInventoryComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(bots.Length);

        foreach (var bot in bots)
        {
            packet.WriteInteger(bot.Id);
            packet.WriteString(bot.Name);
            packet.WriteString(bot.Motto);
            packet.WriteString(bot.Gender);
            packet.WriteString(bot.Figure);
        }
    }
}
