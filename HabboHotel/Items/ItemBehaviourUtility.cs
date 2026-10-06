using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

internal static class ItemBehaviourUtility
{
    public static bool ShouldStackInInventory(this InventoryItem item)
    {
        if (item.IsLimited()) return false;
        return item.Definition.AllowInventoryStack;
    }

    public static bool IsLimited(this InventoryItem item) => item.UniqueSeries > 0;

    /// <summary>
    /// The habbo whose inventory holds the item owns it (items.user_id); InventoryItem.OwnerId can be stale after a
    /// trade or zero after a pickup.
    /// </summary>
    public static Item ToRoomObject(this InventoryItem item, Habbo owner) => new()
    {
        Id = item.Id,
        OwnerId = (uint)owner.Id,
        UserId = owner.Id,
        Username = owner.Username,
        Definition = item.Definition,
        ExtraData = item.ExtraData,
        UniqueNumber = item.UniqueNumber,
        UniqueSeries = item.UniqueSeries,
    };

    public static InventoryItem ToInventoryItem(this Item item) => new()
    {
        Id = item.Id,
        OwnerId = item.OwnerId,
        Definition = item.Definition,
        ExtraData = item.ExtraData,
        UniqueNumber = item.UniqueNumber,
        UniqueSeries = item.UniqueSeries
    };

    public static IOutgoingPacket Serialize(IOutgoingPacket packet, IFurniObjectData stuffData, uint uniqueNumber, uint uniqueSeries)
    {
        var type = (int)stuffData.StructureType;
        if (uniqueSeries > 0)
        {
            type |= 0xFF00;
        }

        packet.WriteInt(type);
        if (stuffData.StructureType != FurniDataStructure.Empty)
        {
            switch (stuffData)
            {
                case LegacyDataFormat legacyData:
                    Serialize(packet, legacyData);
                    break;
                case MapDataFormat mapData:
                    Serialize(packet, mapData);
                    break;
                case StringArrayDataFormat stringArray:
                    Serialize(packet, stringArray);
                    break;
                case VoteResultDataFormat voteResult:
                    Serialize(packet, voteResult);
                    break;
                case IntArrayDataFormat intArray:
                    Serialize(packet, intArray);
                    break;
                case HighscoreDataFormat highScore:
                    Serialize(packet, highScore);
                    break;
                case CrackableDataFormat crackable:
                    Serialize(packet, crackable);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        if (uniqueSeries <= 0) return packet;
        packet.WriteUInt(uniqueNumber);
        packet.WriteUInt(uniqueSeries);
        return packet;
    }

    public static IOutgoingPacket Serialize(IOutgoingPacket packet, LegacyDataFormat data)
    {
        packet.WriteString(data.Data);
        return packet;
    }

    public static IOutgoingPacket Serialize(IOutgoingPacket packet, MapDataFormat data)
    {
        var count = data.Data.Count;
        packet.WriteInt(count);
        foreach (var (key, value) in data.Data)
        {
            packet.WriteString(key);
            packet.WriteString(value);
        }
        return packet;
    }
    public static IOutgoingPacket Serialize(IOutgoingPacket packet, StringArrayDataFormat data)
    {
        var count = data.Data.Count;
        packet.WriteInt(count);
        foreach (var value in data.Data)
            packet.WriteString(value);
        return packet;
    }

    public static IOutgoingPacket Serialize(IOutgoingPacket packet, VoteResultDataFormat data)
    {
        packet.WriteString(data.State);
        packet.WriteInt(data.Result);
        return packet;
    }

    public static IOutgoingPacket Serialize(IOutgoingPacket packet, IntArrayDataFormat data)
    {
        var count = data.Data.Count;
        packet.WriteInt(count);
        foreach (var value in data.Data)
            packet.WriteInt(value);
        return packet;
    }

    public static IOutgoingPacket Serialize(IOutgoingPacket packet, HighscoreDataFormat data)
    {
        packet.WriteString(data.State);
        packet.WriteUInt(data.ScoreType);
        packet.WriteUInt(data.ClearType);
        packet.WriteUInt(0);
        return packet;
    }

    public static IOutgoingPacket Serialize(IOutgoingPacket packet, CrackableDataFormat data)
    {
        packet.WriteString(data.State);
        packet.WriteUInt(data.Hits);
        packet.WriteUInt(data.Target);
        return packet;
    }
}