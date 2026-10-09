using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public enum WiredRoomEntryMethod
{
    Default = 0, Teleport = 2, RoomNetwork = 3
}

public readonly record struct WiredRoomEntrySnapshot(WiredRoomEntryMethod Method, uint TeleporterId, uint SourceRoomId = 0)
{
    internal static WiredRoomEntrySnapshot Capture(Room room, Habbo player)
    {
        var forwarded = player.WiredRoomNetworkDestination;
        var source = player.WiredRoomEntryDestinationRoomId == room.Id ? player.WiredRoomEntrySourceRoomId : 0;
        player.WiredRoomEntrySourceRoomId = 0;
        player.WiredRoomEntryDestinationRoomId = 0;
        player.WiredRoomNetworkDestination = 0;
        var id = player.IsTeleporting ? player.TeleporterId : player.IsHopping ? player.HopperId : 0;

        if (id != 0 && room.GetRoomItemHandler().GetItem(id) is { } destination) {
            return new(WiredRoomEntryMethod.Teleport, destination.Id, source);
        }

        return forwarded != 0 && forwarded == room.Id ? new(WiredRoomEntryMethod.RoomNetwork, 0, source) : default;
    }
}
