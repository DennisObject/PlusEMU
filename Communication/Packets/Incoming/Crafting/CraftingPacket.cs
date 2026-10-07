using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Crafting;

internal static class CraftingPacket
{
    public static uint[] ReadItems(IIncomingPacket packet)
    {
        var count = packet.ReadInt();
        if (count < 0 || count > 50 || count > (packet.Stream.Length - packet.Stream.Position) / sizeof(int)) {
            return [0];
        }
        var items = new uint[count];
        for (var index = 0; index < count; index++) {
            items[index] = packet.ReadUInt();
        }
        return items;
    }
}
