namespace Plus.HabboHotel.Rooms.Rollers;

// Simultaneous group contract (§14.9): validate, reserve, commit without callbacks, publish once,
// then emit the captured slides and dispatch hooks in roller-id order. Any failure is a whole-group no-op.
internal sealed class TransportGroupCommitter(Room room, IRollerTransportEngine engine, TransportGroupValidator validator)
{
    internal bool TryCommit(TransportGroup group)
    {
        if (!validator.IsValid(group) || !engine.Reserve(group)) return false;
        var ordered = group.Moves.OrderBy(move => move.Roller.Id).ToList();
        var actors = ordered.Where(move => move.Actor != null).ToList();
        foreach (var move in ordered.Where(move => move.Cargo != null))
            room.GetRoomItemHandler().SetFloorItem(move.Cargo!, move.Destination.X, move.Destination.Y, move.CarriedZ);
        engine.CommitActors(actors);
        engine.Publish(actors);
        room.SendPacket(ordered.Select(move => move.Slide).ToList());
        foreach (var move in actors.Where(StillLanded)) engine.Land(move);
        return true;
    }

    // An earlier hook in this group may already have moved the actor elsewhere.
    private static bool StillLanded(RollerMove move)
        => move.Actor!.X == move.Destination.X && move.Actor.Y == move.Destination.Y;
}
