using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public static class RoomEngineSerializers
{
    public static void SerializeOwners(this IOutgoingPacket packet, IEnumerable<int> userIds, Room room)
    {
        var owners = userIds.Distinct().ToArray();
        packet.WriteInteger(owners.Length);
        foreach (var userId in owners)
        {
            var username = userId == room.OwnerId
                ? room.OwnerName
                : room.GetRoomUserManager().GetRoomUserByHabbo(userId)?.GetClient()?.GetHabbo()?.Username ?? string.Empty;
            packet.WriteInteger(userId);
            packet.WriteString(username);
        }
    }

    public static void Serialize(this IOutgoingPacket packet, Item item)
    {
        packet.WriteUInteger(item.Id);
        packet.WriteInteger(item.Definition.SpriteId);
        packet.WriteInteger(item.GetX);
        packet.WriteInteger(item.GetY);
        packet.WriteInteger(item.Rotation);
        packet.WriteString(FormattableString.Invariant($"{item.GetZ}"));
        packet.WriteString(FormattableString.Invariant($"{item.Definition.Height}"));
        packet.WriteInteger(GetFloorItemExtra(item));
        ItemBehaviourUtility.Serialize(packet, item.ExtraData, item.UniqueNumber, item.UniqueSeries);
        packet.WriteInteger(-1); // to-do: check
        packet.WriteInteger(item.Definition.Modes > 1 ? 1 : 0);
        packet.WriteInteger(item.UserId);
    }
    public static void Serialize(this IOutgoingPacket packet, ICollection<Item> items)
    {
        packet.WriteInt(items.Count);
        foreach (var item in items)
            packet.Serialize(item);
    }

    private static int GetFloorItemExtra(Item item)
    {
        if (item.Definition.InteractionType == InteractionType.Gift)
        {
            // Gift purchases store ribbon and color as the last two legacy-data fields.
            var data = item.LegacyDataString.Split('\u0005');
            if (data.Length == 7 && int.TryParse(data[5], out var ribbon) && int.TryParse(data[6], out var color))
                return color * 1000 + ribbon;
        }
        return 1;
    }
}