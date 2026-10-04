using System.Drawing;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Rollers;

// Snapshots each roller's cargo and rider once per cycle, with the legacy carried-Z formula.
internal sealed class RollerLoadBuilder(Room room, IRollerTransportEngine engine)
{
    private const int MaxCargo = 10;

    internal IReadOnlyList<RollerLoad> Build(IEnumerable<Item> rollers)
    {
        var cargoSeen = new HashSet<uint>();
        var actorsSeen = new HashSet<RoomUser>(ReferenceEqualityComparer.Instance);
        var loads = new List<RollerLoad>();
        foreach (var roller in rollers)
        {
            if (roller == null || roller.SquareInFront == new Point(roller.GetX, roller.GetY)) continue;
            var load = Load(roller, cargoSeen, actorsSeen);
            if (load.Moves.Count > 0) loads.Add(load);
        }
        engine.RefreshCapabilities(actorsSeen);
        return loads;
    }

    private RollerLoad Load(Item roller, HashSet<uint> cargoSeen, HashSet<RoomUser> actorsSeen)
    {
        var map = room.GetGameMap();
        var destination = roller.SquareInFront;
        var nextIsRoller = map.GetAllRoomItemForSquare(destination.X, destination.Y).Any(item => item.IsRoller);
        var moves = map.GetRoomItemForSquare(roller.GetX, roller.GetY, roller.GetZ)
            .Where(cargo => engine.RestsOnRoller(roller, cargo)).Take(MaxCargo)
            .Where(cargo => cargoSeen.Add(cargo.Id))
            .Select(cargo => CargoMove(roller, cargo, destination, nextIsRoller)).ToList();
        var actor = map.GetRoomUsers(new(roller.GetX, roller.GetY)).FirstOrDefault(user => engine.RestsOnRoller(roller, user));
        if (actor != null && engine.CanRide(actor) && actorsSeen.Add(actor))
            moves.Add(ActorMove(roller, actor, destination, nextIsRoller));
        return new(roller, destination, moves);
    }

    // The setter's own height adjustment is applied here, so the captured slide matches the commit.
    private RollerMove CargoMove(Item roller, Item cargo, Point destination, bool nextIsRoller)
    {
        var z = room.GetRoomItemHandler().ResolveFloorZ(cargo, destination.X, destination.Y,
            Carried(cargo.GetZ, roller, nextIsRoller));
        var slide = new SlideObjectBundleComposer(cargo.GetX, cargo.GetY, cargo.GetZ, destination.X, destination.Y,
            z, roller.Id, 0, cargo.Id);
        return new(roller, new(roller.GetX, roller.GetY), destination, Snapshot(roller, cargo.GetZ, 0, cargo.Rotation), z, cargo, null, slide);
    }

    private static RollerMove ActorMove(Item roller, RoomUser actor, Point destination, bool nextIsRoller)
    {
        var z = Carried(actor.Z, roller, nextIsRoller);
        var slide = new SlideObjectBundleComposer(actor.X, actor.Y, actor.Z, destination.X, destination.Y,
            z, roller.Id, actor.VirtualId, 0);
        return new(roller, new(roller.GetX, roller.GetY), destination,
            Snapshot(roller, actor.Z, actor.Movement.LocationRevision), z, null, actor, slide);
    }

    private static RollerSnapshot Snapshot(Item roller, double sourceZ, long actorRevision, int cargoRotation = 0)
        => new(roller.Rotation, roller.GetZ, sourceZ, actorRevision, cargoRotation);

    private static double Carried(double z, Item roller, bool nextIsRoller) => nextIsRoller ? z : z - roller.Definition.Height;
}
