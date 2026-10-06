using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Plus.HabboHotel.Rooms;

public sealed record RoomUserStatusSnapshot(int VirtualId, int X, int Y, string Z, int HeadRotation,
    int BodyRotation, string Status)
{
    public static ImmutableArray<RoomUserStatusSnapshot> Capture(IEnumerable<RoomUser> users) =>
        users.Select(Capture).ToImmutableArray();

    private static RoomUserStatusSnapshot Capture(RoomUser user)
    {
        var statusText = new StringBuilder("/");

        foreach (var status in user.Statusses.ToList()) {
            statusText.Append(status.Key);

            if (!string.IsNullOrEmpty(status.Value)) {
                statusText.Append(' ').Append(status.Value);
            }

            statusText.Append('/');
        }

        statusText.Append('/');

        return new(user.VirtualId, user.X, user.Y, user.Z.ToString(CultureInfo.InvariantCulture),
            user.RotHead, user.RotBody, statusText.ToString());
    }
}
