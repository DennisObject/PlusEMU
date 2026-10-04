using System.Drawing;
using Plus.Communication.Packets;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Rooms;

// Compatibility transport retains legacy registry order and its dormant clearance.
// Shared clearance and simultaneous groups belong to the stacked planner layer.
internal sealed class RollerCycle
{
    private readonly Room _room;
    private readonly RoomItemHandling _handler;
    private readonly List<uint> _itemsMoved;
    private readonly List<int> _usersMoved;
    private readonly List<IServerPacket> _messages;
    private readonly HashSet<uint> _visited = new();
    private readonly Dictionary<RoomUser, double> _initialZ;
    private readonly RollerTransport _transport;

    internal RollerCycle(Room room, RoomItemHandling handler, List<uint> itemsMoved,
        List<int> usersMoved, List<IServerPacket> messages)
    {
        _room = room; _handler = handler; _itemsMoved = itemsMoved;
        _usersMoved = usersMoved; _messages = messages;
        _initialZ = room.GetRoomUserManager().GetUserList().ToDictionary(actor => actor, actor => actor.Z);
        _transport = new(room, room.GetGameMap().Navigation!);
    }

    internal void Run(IEnumerable<Item> rollers)
    {
        foreach (var roller in rollers) Process(roller);
    }

    private void Process(Item roller)
    {
        if (!_visited.Add(roller.Id)) return;
        var destination = roller.SquareInFront;
        var target = ItemsAt(destination);
        var nextIsRoller = target.Any(item => item.Definition.InteractionType == InteractionType.Roller);
        MoveFurniture(roller, destination, nextIsRoller);
        MoveUser(roller, destination, nextIsRoller);
    }

    private List<Item> ItemsAt(Point tile)
        => _room.GetGameMap().GetAllRoomItemForSquare(tile.X, tile.Y).ToList();

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
            map.Navigation!.ApplyDirty();
        }
    }

    private void MoveUser(Item roller, Point destination, bool nextIsRoller)
    {
        var actor = _room.GetGameMap().GetRoomUsers(roller.Coordinate).FirstOrDefault();
        if (actor == null || actor.IsWalking || _usersMoved.Contains(actor.HabboId)) return;
        var sourceZ = _initialZ.GetValueOrDefault(actor, actor.Z);
        var z = nextIsRoller ? sourceZ : sourceZ - roller.Definition.Height;
        var message = _transport.TryTransport(actor, roller, destination, sourceZ, z);
        if (message == null) return;
        _usersMoved.Add(actor.HabboId); _messages.Add(message);
    }


}
