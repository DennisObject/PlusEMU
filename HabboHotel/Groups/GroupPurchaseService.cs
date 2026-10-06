using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

public readonly record struct GroupPurchaseBadgePart(int Symbol, int Colour, int Position);

public sealed record GroupPurchaseRequest(
    string Name,
    string Description,
    uint RoomId,
    int MainColour,
    int SecondaryColour,
    ImmutableArray<GroupPurchaseBadgePart> BadgeParts);

[Singleton]
public interface IGroupPurchaseService
{
    Task Purchase(GameClient session, GroupPurchaseRequest request);
}

public sealed class GroupPurchaseService(
    IGroupManager groups,
    IRoomDataLoader rooms,
    IWordFilterManager wordFilter,
    ISettingsManager settings,
    IAccountSessionGate accounts,
    ILogger<GroupPurchaseService> logger) : IGroupPurchaseService
{
    private const string FailureMessage =
        "An error occured whilst trying to create this group.\n\nTry again. If you get this message more than once, report it at the link below.\r\rhttp://boonboards.com";

    public async Task Purchase(GameClient session, GroupPurchaseRequest request)
    {
        var habbo = session.GetHabbo();
        using var account = await accounts.EnterAsync(habbo.Id);
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed || ClubAccess.LevelFor(habbo.Access) == 0 ||
                !TryPrepare(request, habbo.Id, out var prepared, out var room, out var cost))
                return;

            if (habbo.Credits < cost)
            {
                session.Send(new BroadcastMessageAlertComposer(
                    $"A group costs {cost} credits! You only have {habbo.Credits}!"));
                return;
            }

            Group group;
            try
            {
                if (!groups.TryCreateGroup(habbo, prepared.Name, prepared.Description, request.RoomId,
                        prepared.Badge, request.MainColour, request.SecondaryColour, out group))
                {
                    session.SendNotification(FailureMessage);
                    return;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to create group for user {UserId} in room {RoomId}",
                    habbo.Id, request.RoomId);
                session.SendNotification(FailureMessage);
                return;
            }

            habbo.Credits = checked(habbo.Credits - cost);
            session.Send(new CreditBalanceComposer(habbo.Credits));
            session.Send(new PurchaseOKComposer());
            room.GroupId = group.Id;
            room.Group = group;
            if (habbo.CurrentRoom?.Data != room)
                session.Send(new RoomForwardComposer(room.Id));
            session.Send(new NewGroupInfoComposer(room.Id, group.Id));
        }
    }

    private bool TryPrepare(GroupPurchaseRequest request, int userId, out PreparedGroup prepared,
        out RoomData room, out int cost)
    {
        prepared = default;
        room = null!;
        cost = 0;
        if (!rooms.TryGetData(request.RoomId, out room) || room.OwnerId != userId ||
            room.GroupId != 0 || room.Group != null ||
            !int.TryParse(settings.TryGetValue("catalog.group.purchase.cost"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out cost) || cost < 0 ||
            !TryBuildBadge(request.BadgeParts, out var badge))
            return false;

        prepared = new(
            wordFilter.CheckMessage(request.Name),
            wordFilter.CheckMessage(request.Description),
            badge);
        return true;
    }

    private static bool TryBuildBadge(ImmutableArray<GroupPurchaseBadgePart> parts, out string badge)
    {
        badge = string.Empty;
        if (parts.IsDefaultOrEmpty || parts.Length > 5)
            return false;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            badge += BadgePartUtility.WorkBadgeParts(index == 0,
                part.Symbol.ToString(CultureInfo.InvariantCulture),
                part.Colour.ToString(CultureInfo.InvariantCulture),
                part.Position.ToString(CultureInfo.InvariantCulture));
        }
        return !string.IsNullOrWhiteSpace(badge);
    }

    private readonly record struct PreparedGroup(string Name, string Description, string Badge);
}
