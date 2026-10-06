namespace Plus.HabboHotel.Groups;

public readonly record struct FavouriteGroupSnapshot(int VirtualId, int GroupId, string Name)
{
    public static FavouriteGroupSnapshot Capture(Group? group, int virtualId) =>
        new(virtualId, group?.Id ?? 0, group?.Name ?? string.Empty);
}

public readonly record struct GroupFurniSettingsSnapshot(uint ItemId, int GroupId, string Name, uint RoomId, bool IsMember, bool ForumEnabled)
{
    public static GroupFurniSettingsSnapshot Capture(Group group, uint itemId, int viewerId) =>
        new(itemId, group.Id, group.Name, group.RoomId, group.IsMember(viewerId), group.ForumEnabled);
}
