namespace Plus.HabboHotel.Rooms.PathFinding;

// Simultaneous group contract (§14.9): validate (including every furniture-setter rejection), reserve,
// commit positions and furniture without callbacks, publish once, settle deferred furniture effects,
// then emit the captured slides and dispatch hooks in roller-id order. Any failure is a whole-group no-op.
internal sealed class TransportGroupCommitter(Room room, RollerTransport transport, TransportGroupValidator validator)
{
    internal bool TryCommit(TransportGroup group)
    {
        var ordered = group.Moves.OrderBy(move => move.Roller.Id).ToList();
        var actors = ordered.Where(move => move.Actor != null).ToList();
        var furniture = ordered.Where(move => move.Cargo != null)
            .Select(move => new FloorMove(move.Cargo!, move.Destination.X, move.Destination.Y, move.CarriedZ)).ToList();
        var items = room.GetRoomItemHandler();
        transport.RefreshCapabilities(actors.Select(move => move.Actor!));
        // Final validation and the positional commit see one placement state; no concurrent move or
        // rotation can land between them.
        lock (room.GetGameMap().PlacementSync)
        {
            if (!validator.IsValid(group) || !transport.Reserve(group)) return false;
            items.CommitFloorMoves(furniture);
            transport.CommitActors(actors);
        }
        transport.Publish(actors);
        items.SettleFloorMoves(furniture);
        room.SendPacket(ordered.Select(move => move.Slide).ToList());
        foreach (var move in actors.Where(StillLanded)) transport.Land(move);
        return true;
    }

    // An earlier hook in this group may already have moved the actor elsewhere.
    private static bool StillLanded(RollerMove move)
        => move.Actor!.X == move.Destination.X && move.Actor.Y == move.Destination.Y;
}
