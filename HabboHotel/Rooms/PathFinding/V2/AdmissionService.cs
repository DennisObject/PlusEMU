namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class AdmissionService(Room room, MovementContext context, ForcePlacementService placement)
{
    public void Admit(RoomUser actor)
    {
        var state = actor.Movement;
        if (state.State == NavState.Active) return;
        state.State = NavState.Active;
        placement.Bind(actor, actor.Z, ForceResolution.ExactZ);
        room.GetGameMap().AddUserToMap(actor, new(actor.X, actor.Y));
        context.RefreshMembership(actor);
        actor.UpdateNeeded = true;
    }
}
