using System.Collections.Immutable;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms;

public sealed record GuestRoomGroupSnapshot(int Id, string Name, string Badge);

public sealed record GuestRoomPromotionSnapshot(string Name, string Description, int MinutesLeft);

public sealed record GuestRoomInfoSnapshot(
    bool IsLoading,
    uint Id,
    string Name,
    int OwnerId,
    string OwnerName,
    int Access,
    int UsersNow,
    int UsersMax,
    string Description,
    int TradeSettings,
    int Score,
    int Category,
    ImmutableArray<string> Tags,
    GuestRoomGroupSnapshot? Group,
    GuestRoomPromotionSnapshot? Promotion,
    bool CheckEntry,
    int WhoCanMute,
    int WhoCanKick,
    int WhoCanBan,
    bool CanModify,
    int ChatMode,
    int ChatSize,
    int ChatSpeed,
    int ExtraFlood,
    int ChatDistance);

public interface IGuestRoomInfoService
{
    GuestRoomInfoSnapshot? Capture(uint roomId, Habbo viewer, bool isLoading, bool checkEntry);
}

public sealed class GuestRoomInfoService(IRoomDataLoader rooms, TimeProvider clock) : IGuestRoomInfoService
{
    public GuestRoomInfoSnapshot? Capture(uint roomId, Habbo viewer, bool isLoading, bool checkEntry)
    {
        if (!rooms.TryGetData(roomId, out var data))
        {
            return null;
        }

        return Capture(data, viewer, isLoading, checkEntry, clock);
    }

    internal static GuestRoomInfoSnapshot Capture(RoomData data, Habbo viewer, bool isLoading, bool checkEntry, TimeProvider clock) =>
        Capture(data, viewer, isLoading, checkEntry, clock.GetUtcNow());

    internal static GuestRoomInfoSnapshot Capture(RoomData data, Habbo viewer, bool isLoading, bool checkEntry, DateTimeOffset now)
    {
        var group = data.Group;
        var promotion = data.Promotion;

        return new(
            isLoading,
            data.Id,
            data.Name,
            data.OwnerId,
            data.OwnerName,
            RoomAccessUtility.GetRoomAccessPacketNum(data.Access),
            data.UsersNow,
            data.UsersMax,
            data.Description,
            data.TradeSettings,
            data.Score,
            data.Category,
            data.Tags.ToImmutableArray(),
            group == null ? null : new(group.Id, group.Name, group.Badge),
            promotion == null ? null : new(promotion.Name, promotion.Description, promotion.MinutesLeftAt(now)),
            checkEntry,
            data.WhoCanMute,
            data.WhoCanKick,
            data.WhoCanBan,
            viewer.Access.Can(PermissionKeys.ModerationTool) || data.OwnerName == viewer.Username,
            data.ChatMode,
            data.ChatSize,
            data.ChatSpeed,
            data.ExtraFlood,
            data.ChatDistance);
    }
}
