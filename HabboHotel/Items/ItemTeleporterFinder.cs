using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items;

public static class ItemTeleporterFinder
{
    public static uint GetTeleRoomId(uint teleId, Room pRoom, IItemTravelStore store)
    {
        if (pRoom.GetRoomItemHandler().GetItem(teleId) != null)
        {
            return pRoom.RoomId;
        }

        return store.FindItemRoom(teleId);
    }

    public static bool IsTeleLinked(uint teleId, Room pRoom, IItemTravelStore store)
    {
        var linkId = store.FindLinkedTeleporter(teleId);

        if (linkId == 0)
        {
            return false;
        }

        var item = pRoom.GetRoomItemHandler().GetItem(linkId);

        if (item != null && item.Definition.InteractionType == InteractionType.Teleport)
        {
            return true;
        }

        var roomId = GetTeleRoomId(linkId, pRoom, store);

        if (roomId == 0)
        {
            return false;
        }

        return true;
    }
}
