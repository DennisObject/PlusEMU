using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Groups;

public readonly record struct GroupBadgePiece(int Symbol, int Colour, int Position);
public sealed record GroupManagementSnapshot(
    bool HasRoom, uint RoomId, string RoomName, int GroupId, string Name, string Description,
    int Colour1, int Colour2, int Type, int AdminOnlyDeco, ImmutableArray<GroupBadgePiece> BadgePieces,
    string Badge, int MemberCount, bool ForumEnabled);

public interface IGroupManagementSnapshotService
{
    void Send(GameClient session, int groupId);
}

public sealed class GroupManagementSnapshotService(IGroupManager groups, IRoomDataLoader rooms) : IGroupManagementSnapshotService
{
    public void Send(GameClient session, int groupId)
    {
        if (!groups.TryGetGroup(groupId, out var group)) {
            return;
        }

        var habbo = session.GetHabbo();

        if (group.CreatorId != habbo.Id && !habbo.Access.Can(PermissionKeys.GroupManagementOverride)) {
            return;
        }

        if (!TryParseBadge(group.Badge, out var pieces)) {
            return;
        }

        var roomName = group.RoomId != 0 && rooms.TryGetData(group.RoomId, out var roomData) ? roomData.Name : string.Empty;
        var snapshot = new GroupManagementSnapshot(
            group.RoomId != 0, group.RoomId, roomName, group.Id, group.Name, group.Description,
            group.Colour1, group.Colour2, group.Type switch { GroupType.Open => 0, GroupType.Locked => 1, _ => 2 },
            group.AdminOnlyDeco, pieces, group.Badge, group.MemberCount, group.ForumEnabled);
        session.Send(new ManageGroupComposer(snapshot));
    }

    internal static bool TryParseBadge(string? badge, out ImmutableArray<GroupBadgePiece> pieces)
    {
        var parsed = new List<GroupBadgePiece>(5);
        var symbols = (badge ?? string.Empty).Replace("b", string.Empty, StringComparison.Ordinal).Split('s', StringSplitOptions.RemoveEmptyEntries);

        if (symbols.Length > 5) {
            pieces = [];

            return false;
        }

        foreach (var symbol in symbols) {
            if (symbol.Length is < 4 or > 6) {
                pieces = [];

                return false;
            }

            var symbolLength = symbol.Length >= 6 ? 3 : 2;
            var position = 0;

            if (!AsciiDigits(symbol) || !int.TryParse(symbol.AsSpan(0, symbolLength), out var symbolId) ||
                !int.TryParse(symbol.AsSpan(symbolLength, 2), out var colour) ||
                (symbol.Length >= 5 && !int.TryParse(symbol.AsSpan(symbol.Length - 1, 1), out position))) {
                pieces = [];

                return false;
            }

            parsed.Add(new(symbolId, colour, position));
        }

        while (parsed.Count < 5) {
            parsed.Add(default);
        }

        pieces = [.. parsed];

        return true;
    }

    private static bool AsciiDigits(string value) => value.All(character => character is >= '0' and <= '9');
}
