using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Rollers;

// Engine-specific actor rules and commit steps. Both engines share planning, furniture rules and ordering.
internal interface IRollerTransportEngine
{
    bool CanRide(RoomUser actor);

    // What a roller carries from its tile: the legacy engine and K=1 carry everything there; layered
    // rooms carry only actors and items resting on the roller's own surface.
    bool RestsOnRoller(Item roller, RoomUser actor);
    bool RestsOnRoller(Item roller, Item cargo);

    // Snapshot actor capabilities (they may consult other services) before any placement lock is taken.
    void RefreshCapabilities(IEnumerable<RoomUser> actors);

    bool AdmitsActor(RollerMove move, RollerDepartures departing);

    // Engine occupancy that furniture may not land on anywhere in its footprint, beyond the shared rules.
    bool AdmitsCargo(RollerMove move, IRollerDepartureView departures);

    // Collective destination reservations, held until the end of the user phase.
    bool Reserve(TransportGroup group);

    // Positions and membership only: no hooks may run here.
    void CommitActors(IReadOnlyList<RollerMove> moves);

    // Publish the complete final geometry once, then bind the moved actors to it.
    void Publish(IReadOnlyList<RollerMove> moves);

    void Land(RollerMove move);
}
