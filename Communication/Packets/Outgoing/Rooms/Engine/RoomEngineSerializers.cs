using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public static class RoomEngineSerializers
{
    public static void Serialize(this IOutgoingPacket packet, Item item)
    {
        packet.WriteUInteger(item.Id);
        packet.WriteInteger(item.Definition.SpriteId);
        packet.WriteInteger(item.GetX);
        packet.WriteInteger(item.GetY);
        packet.WriteInteger(item.Rotation);
        packet.WriteString(FormattableString.Invariant($"{item.GetZ}"));
        packet.WriteString(FormattableString.Invariant($"{item.Definition.Height}"));
        packet.WriteInteger(FloorExtra(item));
        ItemBehaviourUtility.Serialize(packet, item);
        packet.WriteInteger(-1); // to-do: check
        packet.WriteInteger(item.Definition.Modes > 1 ? 1 : 0);
        packet.WriteInteger(item.UserId);
        WriteFurnitureMetadata(packet, item);
    }
    public static void Serialize(this IOutgoingPacket packet, ICollection<Item> items)
    {
        packet.WriteInt(items.Count);
        foreach (var item in items)
            packet.Serialize(item);
    }

    internal static void WriteFurnitureMetadata(IOutgoingPacket packet, Item item)
    {
        packet.WriteInteger(item.Definition.Stackable ? 1 : 0);
        packet.WriteInteger(item.Definition.IsSeat ? 1 : 0);
        packet.WriteInteger(item.Definition.InteractionType == InteractionType.Bed ? 1 : 0);
        packet.WriteInteger(item.Definition.Walkable ? 1 : 0);
        packet.WriteInteger(item.Definition.Width);
        packet.WriteInteger(item.Definition.Length);
        packet.WriteInteger(0); // No linked teleport target is advertised.
    }

    internal static void WriteOwnerMap(IOutgoingPacket packet, IEnumerable<Item> items, int roomOwnerId, string? roomOwnerName)
    {
        var names = new Dictionary<int, string>();
        foreach (var item in items)
        {
            var name = item.UserId == roomOwnerId ? roomOwnerName ?? "" : item.Username ?? "";
            if (!names.TryGetValue(item.UserId, out var existing) || (existing.Length == 0 && name.Length > 0))
                names[item.UserId] = name;
        }

        packet.WriteInteger(names.Count);
        if (names.Remove(roomOwnerId, out var ownerName))
        {
            packet.WriteInteger(roomOwnerId);
            packet.WriteString(ownerName);
        }

        foreach (var userId in names.Keys.OrderBy(id => id))
        {
            packet.WriteInteger(userId);
            packet.WriteString(names[userId]);
        }
    }

    internal static int FloorExtra(Item item)
    {
        if (item.Definition.InteractionType == InteractionType.WalkMagicTile)
            return MagicTileHeight.MultiWalk(item) ? 1 : 0;
        if (item.Definition.InteractionType == InteractionType.Gift)
            return GiftWrap.Style(item.LegacyDataString);
        else if (item.Definition.InteractionType == InteractionType.MusicDisc)
        {
            var fields = item.LegacyDataString.Split('\n');
            if (fields.Length >= 7 && int.TryParse(fields[6], out var songId))
                return songId;
        }

        return 1;
    }
}