namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class MovementProfileService(Room room, NavGrid grid, PathfindingSettings settings,
    ActorAccessResolver access)
{
    public MovementProfileService(Room room, NavGrid grid, PathfindingSettings settings, Func<int, int, bool> isMember)
        : this(room, grid, settings, new ActorAccessResolver(isMember))
    {
    }

    private int _groupVersion = -1;
    private int[] _groups = [];

    public ActorProfile Refresh(RoomUser actor)
    {
        var profile = actor.Movement.Profile;
        var temporary = actor.BotData?.IsTemporary == true;
        profile.LegacyOverride = actor.AllowOverride && !temporary;
        profile.IgnoreUsers = temporary;
        profile.IgnoreStepHeight = actor.RidingHorse && settings.RidersIgnoreHeight;
        profile.Walkthrough = room.RoomBlockingEnabled;
        profile.DiagonalEnabled = room.GetGameMap().DiagonalEnabled;

        if (_groupVersion != grid.Version) {
            RefreshGroups();
        }

        if (_groups.Length == 0) {
            return profile;
        }

        access.Refresh(profile, actor.GetClient()?.GetHabbo()?.Id, _groups);

        return profile;
    }

    private void RefreshGroups()
    {
        _groups = grid.GroupId.Where(group => group != 0).Distinct().ToArray();
        _groupVersion = grid.Version;
    }
}
