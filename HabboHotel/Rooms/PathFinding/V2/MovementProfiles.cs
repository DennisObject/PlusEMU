namespace Plus.HabboHotel.Rooms.PathFinding;

public static class MovementProfiles
{
    public static ActorProfile Refresh(Room room, NavGrid grid, PathfindingSettings settings, RoomUser actor)
    {
        var profile = actor.Movement.Profile;
        profile.LegacyOverride = actor.AllowOverride && actor.BotData?.IsTemporary != true;
        profile.IgnoreUsers = actor.BotData?.IsTemporary == true;
        profile.IgnoreStepHeight = actor.RidingHorse && settings.RidersIgnoreHeight;
        profile.Walkthrough = room.RoomBlockingEnabled;
        profile.DiagonalEnabled = room.GetGameMap().DiagonalEnabled;
        var habbo = actor.GetClient()?.GetHabbo();
        foreach (var id in grid.GroupId.Distinct())
            if (id != 0) profile.SetMembership(id, habbo != null && PlusEnvironment.Game.GroupManager.TryGetGroup(id, out var group) && group.IsMember(habbo.Id));
        return profile;
    }
}
