using System.Drawing;
using Plus.Communication.Packets;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Rooms;

// Resolve downstream rollers before checking clearance: only objects which actually
// leave their tile this cycle cease to obstruct an upstream roller.
internal sealed class RollerCycle
{
    private readonly Room _room;
    private readonly RoomItemHandling _handler;
    private readonly List<uint> _itemsMoved;
    private readonly List<int> _usersMoved;
    private readonly List<IServerPacket> _messages;
    private readonly HashSet<uint> _visited = new();
    private readonly Dictionary<RoomUser, double> _initialZ;
    private readonly RollerTransport? _transport;

    internal RollerCycle(Room room, RoomItemHandling handler, List<uint> itemsMoved,
        List<int> usersMoved, List<IServerPacket> messages)
    {
        _room = room; _handler = handler; _itemsMoved = itemsMoved;
        _usersMoved = usersMoved; _messages = messages;
        _initialZ = room.GetRoomUserManager().GetUserList().ToDictionary(actor => actor, actor => actor.Z);
        if (room.GetGameMap().Navigation is { UsesExecutor: true } navigation)
            _transport = new(room, navigation);
    }

    internal void Run(IEnumerable<Item> rollers)
    {
        foreach (var roller in rollers) Process(roller);
    }

    private void Process(Item roller)
    {
        if (!_visited.Add(roller.Id)) return;
        var destination = roller.SquareInFront;
        foreach (var next in ItemsAt(destination).Where(item => item.IsRoller)) Process(next);
        var target = ItemsAt(destination);
        var nextIsRoller = target.Any(item => item.IsRoller);
        if (!Clear(target)) return;
        MoveFurniture(roller, destination, nextIsRoller);
        MoveUser(roller, destination, nextIsRoller);
    }

    private List<Item> ItemsAt(Point tile)
        => _room.GetGameMap().GetAllRoomItemForSquare(tile.X, tile.Y).ToList();

    private static bool Clear(List<Item> items)
    {
        var rollers = items.Where(item => item.IsRoller).ToList();
        if (rollers.Count == 0) return true;
        var top = rollers.Max(item => item.TotalHeight);
        return items.All(item => item.TotalHeight <= top);
    }

    private void MoveFurniture(Item roller, Point destination, bool nextIsRoller)
    {
        var map = _room.GetGameMap();
        var cargo = map.GetRoomItemForSquare(roller.GetX, roller.GetY, roller.GetZ).Take(10).ToList();
        foreach (var item in cargo)
        {
            if (_itemsMoved.Contains(item.Id) || roller.GetZ >= item.GetZ
                || !map.CanRollItemHere(destination.X, destination.Y)
                || _room.GetRoomUserManager().GetUserForSquare(destination.X, destination.Y) != null) continue;
            var z = nextIsRoller ? item.GetZ : item.GetZ - roller.Definition.Height;
            var message = _handler.UpdateItemOnRoller(item, destination, roller.Id, z);
            if (item.GetX != destination.X || item.GetY != destination.Y) continue;
            _itemsMoved.Add(item.Id); _messages.Add(message);
            if (_transport != null) map.Navigation!.ApplyDirty();
        }
    }

    private void MoveUser(Item roller, Point destination, bool nextIsRoller)
    {
        var actor = _room.GetGameMap().GetRoomUsers(roller.Coordinate).FirstOrDefault();
        if (actor == null || actor.IsWalking || _usersMoved.Contains(actor.HabboId)) return;
        var sourceZ = _initialZ.GetValueOrDefault(actor, actor.Z);
        var z = nextIsRoller ? sourceZ : sourceZ - roller.Definition.Height;
        var message = _transport != null
            ? _transport.TryTransport(actor, roller, destination, sourceZ, z)
            : MoveLegacyUser(actor, roller, destination, z);
        if (message == null) return;
        _usersMoved.Add(actor.HabboId); _messages.Add(message);
    }

    private IServerPacket? MoveLegacyUser(RoomUser actor, Item roller, Point destination, double z)
    {
        var map = _room.GetGameMap();
        if (!map.IsValidStep(new(roller.GetX, roller.GetY), new(destination.X, destination.Y), true, false, true)
            || !map.CanRollItemHere(destination.X, destination.Y) || map.GetFloorStatus(destination) == 0) return null;
        actor.IsRolling = true; actor.RollerDelay = 1;
        return _handler.UpdateUserOnRoller(actor, destination, roller.Id, z);
    }
}
