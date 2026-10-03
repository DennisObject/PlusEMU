namespace Plus.HabboHotel.Rooms.Rollers;

// Engine-specific actor rules and commit steps. Both engines share planning, furniture rules and ordering.
internal interface IRollerTransportEngine
{
    bool CanRide(RoomUser actor);

    bool AdmitsActor(RollerMove move, RollerDepartures departing);

    // Collective destination reservations, held until the end of the user phase.
    bool Reserve(TransportGroup group);

    // Positions and membership only: no hooks may run here.
    void CommitActors(IReadOnlyList<RollerMove> moves);

    // Publish the complete final geometry once, then bind the moved actors to it.
    void Publish(IReadOnlyList<RollerMove> moves);

    void Land(RollerMove move);
}
