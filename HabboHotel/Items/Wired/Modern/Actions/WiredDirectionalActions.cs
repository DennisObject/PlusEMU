using System.Drawing;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Turbo's per-item heading and nearest-avatar algorithms; the caller commits actual moves.</summary>
public sealed class WiredDirectionalActions
{
    private readonly Dictionary<Item, int> _headings = [];
    public bool MoveHeading(Item item, int initial, int turn, bool blockUsers,
        Func<int, int, bool> move, Func<int, int, RoomUser[]> usersAt, Action<Item, RoomUser> collision)
    {
        var heading = Heading(item, initial);
        var moved = false;

        for (var attempt = 0; attempt < 8; attempt++) {
            var offset = WiredRoomOperations.Offset(heading);
            var x = item.GetX + offset.X;
            var y = item.GetY + offset.Y;
            var occupants = blockUsers ? usersAt(x, y) : [];

            foreach (var user in occupants.Where(user => !user.IsBot)) {
                collision(item, user);
            }

            if (occupants.Length == 0 && move(x, y)) {
                moved = true;
                break;
            }

            if (turn == 6) {
                break;
            }

            heading = Turn(heading, turn);
        }

        _headings[item] = heading;

        return moved;
    }
    public int Heading(Item item, int initial) => _headings.GetValueOrDefault(item, initial);
    public void Retain(IEnumerable<Item> attached)
    {
        var live = attached.ToHashSet();

        foreach (var item in _headings.Keys.Where(item => !live.Contains(item)).ToArray()) {
            _headings.Remove(item);
        }
    }
    public static int Turn(int direction, int mode) => mode switch
    {
        1 => (direction + 2) % 8,
        2 => (direction + 6) % 8,
        3 => (direction + 1) % 8,
        4 => (direction + 7) % 8,
        5 => Random.Shared.Next(8),
        6 => direction,
        _ => (direction + 4) % 8
    };
    public static RoomUser? Nearest(Item item, IEnumerable<RoomUser> users) => users.Where(user => !user.IsBot)
        .Select(user => (User: user, Distance: Math.Max(Math.Abs(user.X - item.GetX), Math.Abs(user.Y - item.GetY))))
        .Where(pair => pair.Distance <= 3).OrderBy(pair => pair.Distance).Select(pair => pair.User).FirstOrDefault();
    public static IEnumerable<Point> Steps(Item item, RoomUser user, bool away)
    {
        var dx = user.X - item.GetX;
        var dy = user.Y - item.GetY;

        if (away) {
            dx = -dx;
            dy = -dy;
        }

        var x = new Point(item.GetX + Math.Sign(dx), item.GetY);
        var y = new Point(item.GetX, item.GetY + Math.Sign(dy));

        return (Math.Abs(dx) >= Math.Abs(dy) ? new[] { x, y } : [y, x])
            .Where(point => point.X != item.GetX || point.Y != item.GetY);
    }
    public static int AvatarRotation(int body, int raw) => raw switch
    { 8 => (body + 2) % 8, 9 => (body + 6) % 8, _ => raw };
}
