using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.HabboHotel.Rooms;

internal static class RoomAppender
{
    public static RoomWireData Capture(RoomData data, INavigatorManager navigator)
    {
        navigator.TryGetFeaturedRoom(data.Id, out var featured);
        return new(data.Id, data.Name, data.OwnerId, data.OwnerName, RoomAccessUtility.GetRoomAccessPacketNum(data.Access), data.UsersNow, data.UsersMax,
            data.Description, data.TradeSettings, data.Score, data.Category, data.Tags.ToImmutableArray(), data.Type == "private", data.AllowPets,
            featured?.Image, data.Group == null ? null : new(data.Group.Id, data.Group.Name, data.Group.Badge),
            data.Promotion == null ? null : new(data.Promotion.Name, data.Promotion.Description, data.Promotion.MinutesLeft));
    }

    public static void WriteRoom(IOutgoingPacket packet, RoomWireData data)
    {
        packet.WriteUInteger(data.Id);
        packet.WriteString(data.Name);
        packet.WriteInteger(data.OwnerId);
        packet.WriteString(data.OwnerName);
        packet.WriteInteger(data.Access);
        packet.WriteInteger(data.UsersNow);
        packet.WriteInteger(data.UsersMax);
        packet.WriteString(data.Description);
        packet.WriteInteger(data.TradeSettings);
        packet.WriteInteger(data.Score);
        packet.WriteInteger(0); //Top rated room rank.
        packet.WriteInteger(data.Category);
        packet.WriteInteger(data.Tags.Length);
        foreach (var tag in data.Tags) packet.WriteString(tag);
        var roomType = 0;
        if (data.Group != null)
            roomType += 2;
        if (data.Promotion != null)
            roomType += 4;
        if (data.IsPrivate)
            roomType += 8;
        if (data.AllowPets)
            roomType += 16;
        if (data.FeaturedImage != null) roomType += 1;
        packet.WriteInteger(roomType);
        if (data.FeaturedImage != null) packet.WriteString(data.FeaturedImage);
        if (data.Group != null)
        {
            packet.WriteInteger(data.Group.Id);
            packet.WriteString(data.Group.Name);
            packet.WriteString(data.Group.Badge);
        }
        if (data.Promotion != null)
        {
            packet.WriteString(data.Promotion.Name);
            packet.WriteString(data.Promotion.Description);
            packet.WriteInteger(data.Promotion.MinutesLeft);
        }
    }
}

public sealed record RoomGroupWireData(int Id, string Name, string Badge);
public sealed record RoomPromotionWireData(string Name, string Description, int MinutesLeft);
public sealed record RoomWireData(uint Id, string Name, int OwnerId, string OwnerName, int Access, int UsersNow, int UsersMax, string Description,
    int TradeSettings, int Score, int Category, ImmutableArray<string> Tags, bool IsPrivate, bool AllowPets, string? FeaturedImage,
    RoomGroupWireData? Group, RoomPromotionWireData? Promotion);
