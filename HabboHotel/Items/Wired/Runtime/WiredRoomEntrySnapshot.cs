using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public enum WiredRoomEntryMethod
{
    Default = 0, Teleport = 2, RoomNetwork = 3
}

public readonly record struct WiredRoomEntrySnapshot(WiredRoomEntryMethod Method, uint TeleporterId)
{
    internal static WiredRoomEntrySnapshot Capture(Room room, Habbo player)
    {
        var forwarded = player.WiredRoomNetworkDestination;
        player.WiredRoomNetworkDestination = 0;
        var id = player.IsTeleporting ? player.TeleporterId : player.IsHopping ? player.HopperId : 0;

        if (id != 0 && room.GetRoomItemHandler().GetItem(id) is { } destination)
        {
            return new(WiredRoomEntryMethod.Teleport, destination.Id);
        }

        return forwarded != 0 && forwarded == room.Id ? new(WiredRoomEntryMethod.RoomNetwork, 0) : default;
    }
}
