using System.Drawing;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Room-owned last flights follow the exact client animation clock, not another timer or item cycle.</summary>
public sealed class WiredProjectileFlights(Room room)
{
    private static readonly ConditionalWeakTable<Room, WiredProjectileFlights> Rooms = new();
    private readonly Dictionary<uint, Flight> _flights = [];
    public static WiredProjectileFlights For(Room room) => Rooms.GetValue(room, current => new(current));

    public bool Begin(Item item, int sourceX, int sourceY, double sourceZ, int durationMs, long nowMs)
    {
        if (!item.IsFloorItem || !ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item))
        {
            return false;
        }

        var path = BuildPath(sourceX, sourceY, item.GetX, item.GetY);
        var map = room.GetGameMap();

        if (path.Any(point => !map.ValidTile(point.X, point.Y)))
        {
            return false;
        }

        _flights[item.Id] = new(item, sourceX, sourceY, (int)(sourceZ * 100), item.GetX, item.GetY, (int)(item.GetZ * 100),
            nowMs, durationMs,
            path.Select(point => map.GetRoomUsers(point).DistinctBy(user => user.VirtualId).Count()).ToArray(),
            path.Select(point => map.GetCoordinatedItems(point).Where(other => !ReferenceEquals(other, item)).DistinctBy(other => other.Id).Count()).ToArray());

        return true;
    }

    public int? Read(Item item, string key, long nowMs)
    {
        if (!_flights.TryGetValue(item.Id, out var flight))
        {
            return null;
        }

        if (!ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), flight.Item))
        {
            _flights.Remove(item.Id);

            return null;
        }

        if (!ReferenceEquals(flight.Item, item))
        {
            return null;
        }

        var progress = flight.DurationMs <= 0 ? 1 : Math.Clamp((nowMs - flight.StartedAtMs) / (double)flight.DurationMs, 0, 1);
        var travelled = (int)Math.Floor(progress * Math.Max(0, flight.UserCounts.Length - 1));
        int Position(int from, int to) => (int)Math.Round(from + (to - from) * progress);
        int Collisions(int[] counts) => counts.Skip(1).Take(travelled).Sum(); // Launch tile is not crossed into.

        return key switch
        {
            "@projectile.animation.furni_collisions" => Collisions(flight.FurniCounts),
            "@projectile.animation.user_collisions" => Collisions(flight.UserCounts),
            "@projectile.animation.tiles_traveled" => travelled,
            "@projectile.animation.is_traveling" => progress < 1 ? 1 : null,
            "@projectile.animation.position.x" => Position(flight.SourceX, flight.TargetX),
            "@projectile.animation.position.y" => Position(flight.SourceY, flight.TargetY),
            "@projectile.animation.position.altitude" => Position(flight.SourceZ, flight.TargetZ),
            _ => null
        };
    }
    public void Forget(Item item)
    {
        if (_flights.TryGetValue(item.Id, out var flight) && ReferenceEquals(flight.Item, item))
        {
            _flights.Remove(item.Id);
        }
    }
    public void Clear() => _flights.Clear();
    public static IReadOnlyList<Point> BuildPath(int sourceX, int sourceY, int targetX, int targetY)
    {
        var dx = targetX - sourceX;
        var dy = targetY - sourceY;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        var path = new List<Point>(steps + 1);

        for (var step = 0; step <= steps; step++)
        {
            var progress = steps == 0 ? 0d : step / (double)steps;
            path.Add(new((int)Math.Round(sourceX + dx * progress), (int)Math.Round(sourceY + dy * progress)));
        }

        return path;
    }
    private sealed record Flight(Item Item, int SourceX, int SourceY, int SourceZ, int TargetX, int TargetY, int TargetZ,
        long StartedAtMs, int DurationMs, int[] UserCounts, int[] FurniCounts);
}
