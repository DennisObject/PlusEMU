using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
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

    public static void GenerateExtradata(Item item, IOutgoingPacket packet)
    {
        switch (item.Definition.InteractionType)
        {
            default:
                packet.WriteInteger(1);
                packet.WriteInteger(0);
                packet.WriteString(item.Definition.InteractionType != InteractionType.FootballGate ? item.LegacyDataString : string.Empty);
                break;
            case InteractionType.GnomeBox:
                packet.WriteInteger(0);
                packet.WriteInteger(0);
                packet.WriteString("");
                break;
            case InteractionType.PetBreedingBox:
            case InteractionType.PurchasableClothing:
                packet.WriteInteger(0);
                packet.WriteInteger(0);
                packet.WriteString("0");
                break;
            case InteractionType.Stacktool:
                packet.WriteInteger(0);
                packet.WriteInteger(0);
                packet.WriteString("");
                break;
            case InteractionType.Wallpaper:
                packet.WriteInteger(2);
                packet.WriteInteger(0);
                packet.WriteString(item.LegacyDataString);
                break;
            case InteractionType.Floor:
                packet.WriteInteger(3);
                packet.WriteInteger(0);
                packet.WriteString(item.LegacyDataString);
                break;
            case InteractionType.Landscape:
                packet.WriteInteger(4);
                packet.WriteInteger(0);
                packet.WriteString(item.LegacyDataString);
                break;
            case InteractionType.GuildItem:
            case InteractionType.GuildGate:
            case InteractionType.GuildForum:
                Group? group = null;
                if (!PlusEnvironment.Game.GroupManager.TryGetGroup(item.GroupId, out group))
                {
                    packet.WriteInteger(1);
                    packet.WriteInteger(0);
                    packet.WriteString(item.LegacyDataString);
                }
                else
                {
                    packet.WriteInteger(0);
                    packet.WriteInteger(2);
                    packet.WriteInteger(5);
                    packet.WriteString(item.LegacyDataString);
                    packet.WriteString(group.Id.ToString());
                    packet.WriteString(group.Badge);
                    packet.WriteString(PlusEnvironment.Game.GroupManager.GetColourCode(group.Colour1, true));
                    packet.WriteString(PlusEnvironment.Game.GroupManager.GetColourCode(group.Colour2, false));
                }
                break;
            case InteractionType.Background:
                packet.WriteInteger(0);
                packet.WriteInteger(1);
                if (!string.IsNullOrEmpty(item.LegacyDataString))
                {
                    packet.WriteInteger(item.LegacyDataString.Split(Convert.ToChar(9)).Length / 2);
                    for (var i = 0; i <= item.LegacyDataString.Split(Convert.ToChar(9)).Length - 1; i++) packet.WriteString(item.LegacyDataString.Split(Convert.ToChar(9))[i]);
                }
                else
                    packet.WriteInteger(0);
                break;
            case InteractionType.Gift:
            {
                var extraData = item.LegacyDataString.Split(Convert.ToChar(5));
                if (extraData.Length != 7)
                {
                    packet.WriteInteger(0);
                    packet.WriteInteger(0);
                    packet.WriteString(item.LegacyDataString);
                }
                else
                {
                    var style = GiftWrap.Style(item.LegacyDataString);
                    var purchaser = PlusEnvironment.Game.CacheManager.GenerateUser(Convert.ToInt32(extraData[2]));
                    if (purchaser == null)
                    {
                        packet.WriteInteger(0);
                        packet.WriteInteger(0);
                        packet.WriteString(item.LegacyDataString);
                    }
                    else
                    {
                        packet.WriteInteger(style);
                        packet.WriteInteger(1);
                        packet.WriteInteger(6);
                        packet.WriteString("EXTRA_PARAM");
                        packet.WriteString("");
                        packet.WriteString("MESSAGE");
                        packet.WriteString(extraData[1]);
                        packet.WriteString("PURCHASER_NAME");
                        packet.WriteString(purchaser.Username);
                        packet.WriteString("PURCHASER_FIGURE");
                        packet.WriteString(purchaser.Look);
                        packet.WriteString("PRODUCT_CODE");
                        packet.WriteString("A1 KUMIANKKA");
                        packet.WriteString("state");
                        packet.WriteString(item.MagicRemove ? "1" : "0");
                    }
                }
            }
                break;
            case InteractionType.Mannequin:
                packet.WriteInteger(0);
                packet.WriteInteger(1);
                packet.WriteInteger(3);
                if (item.LegacyDataString.Contains(Convert.ToChar(5).ToString()))
                {
                    var stuff = item.LegacyDataString.Split(Convert.ToChar(5));
                    packet.WriteString("GENDER");
                    packet.WriteString(stuff[0]);
                    packet.WriteString("FIGURE");
                    packet.WriteString(stuff[1]);
                    packet.WriteString("OUTFIT_NAME");
                    packet.WriteString(stuff[2]);
                }
                else
                {
                    packet.WriteString("GENDER");
                    packet.WriteString("");
                    packet.WriteString("FIGURE");
                    packet.WriteString("");
                    packet.WriteString("OUTFIT_NAME");
                    packet.WriteString("");
                }
                break;
            case InteractionType.Toner:
                if (item.RoomId != 0)
                {
                    if (item.GetRoom().TonerData == null)
                        item.GetRoom().TonerData = new(item.Id);
                    packet.WriteInteger(0);
                    packet.WriteInteger(5);
                    packet.WriteInteger(4);
                    packet.WriteInteger(item.GetRoom().TonerData.Enabled);
                    packet.WriteInteger(item.GetRoom().TonerData.Hue);
                    packet.WriteInteger(item.GetRoom().TonerData.Saturation);
                    packet.WriteInteger(item.GetRoom().TonerData.Lightness);
                }
                else
                {
                    packet.WriteInteger(0);
                    packet.WriteInteger(0);
                    packet.WriteString(string.Empty);
                }
                break;
            case InteractionType.BadgeDisplay:
                packet.WriteInteger(0);
                packet.WriteInteger(2);
                packet.WriteInteger(4);
                var badgeData = item.LegacyDataString.Split(Convert.ToChar(9));
                if (item.LegacyDataString.Contains(Convert.ToChar(9).ToString()))
                {
                    packet.WriteString("0"); //No idea
                    packet.WriteString(badgeData[0]); //Badge name
                    packet.WriteString(badgeData[1]); //Owner
                    packet.WriteString(badgeData[2]); //Date
                }
                else
                {
                    packet.WriteString("0"); //No idea
                    packet.WriteString("DEV"); //Badge name
                    packet.WriteString("Sledmore"); //Owner
                    packet.WriteString("13-13-1337"); //Date
                }
                break;
            case InteractionType.Television:
                packet.WriteInteger(0);
                packet.WriteInteger(1);
                packet.WriteInteger(1);
                packet.WriteString("THUMBNAIL_URL");
                //Message.WriteString("http://img.youtube.com/vi/" + PlusEnvironment.GetGame().GetTelevisionManager().TelevisionList.OrderBy(x => Guid.NewGuid()).FirstOrDefault().YouTubeId + "/3.jpg");
                packet.WriteString("");
                break;
            case InteractionType.Lovelock:
                if (item.LegacyDataString.Contains(Convert.ToChar(5).ToString()))
                {
                    var eData = item.LegacyDataString.Split((char)5);
                    var I = 0;
                    packet.WriteInteger(0);
                    packet.WriteInteger(2);
                    packet.WriteInteger(eData.Length);
                    while (I < eData.Length)
                    {
                        packet.WriteString(eData[I]);
                        I++;
                    }
                }
                else
                {
                    packet.WriteInteger(0);
                    packet.WriteInteger(0);
                    packet.WriteString("0");
                }
                break;
            case InteractionType.MonsterplantSeed:
                packet.WriteInteger(0);
                packet.WriteInteger(1);
                packet.WriteInteger(1);
                packet.WriteString("rarity");
                packet.WriteString("1"); //Leve should be dynamic.
                break;
        }
    }

    public static void GenerateWallExtradata(Item item, IOutgoingPacket message)
    {
        switch (item.Definition.InteractionType)
        {
            default:
                message.WriteString(item.LegacyDataString);
                break;
            case InteractionType.Postit:
                message.WriteString(item.LegacyDataString.Split(' ')[0]);
                break;
        }
    }

    public static IOutgoingPacket Serialize(IOutgoingPacket packet, Item item)
    {
        var data = MagicTileHeight.IsMagicTile(item.Definition.InteractionType)
            ? new LegacyDataFormat { Data = MagicTileHeight.ToWire(item.GetZ).ToString(System.Globalization.CultureInfo.InvariantCulture) }
            : item.ExtraData;
        return Serialize(packet, data, item.UniqueNumber, item.UniqueSeries);
    }

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