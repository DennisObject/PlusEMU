using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Action;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Users.Ignores;

[Singleton]
public interface IPlayerIgnoreService
{
    Task Ignore(GameClient session, string username);
    Task Unignore(GameClient session, string username);
}

public sealed class PlayerIgnoreService(
    IGameClientManager clients,
    IPlayerIgnoreStore store,
    IAchievementManager achievements,
    IAccountSessionGate accounts) : IPlayerIgnoreService
{
    public Task Ignore(GameClient session, string username) => Change(session, username, true);

    public Task Unignore(GameClient session, string username) => Change(session, username, false);

    private async Task Change(GameClient session, string username, bool ignored)
    {
        var habbo = session.GetHabbo();
        using var held = await accounts.EnterAsync(habbo.Id);
        var room = habbo.CurrentRoom;

        if (room == null) {
            return;
        }

        var target = clients.GetClientByUsername(username)?.GetHabbo();

        if (target == null || (ignored && target.Access.Can(PermissionKeys.ModerationTool))) {
            return;
        }

        if (habbo.IgnoresComponent.IsIgnored(target.Id) == ignored) {
            return;
        }

        var name = await clients.GetNameById(target.Id);

        if (habbo.CurrentRoom != room || !await store.SetIgnored(habbo.Id, target.Id, ignored)) {
            return;
        }

        if (ignored) {
            habbo.IgnoresComponent.PublishIgnore(target.Id);
        }
        else {
            habbo.IgnoresComponent.PublishUnignore(target.Id);
        }

        session.Send(new IgnoreStatusComposer(ignored ? IgnoreStatus.Added : IgnoreStatus.Removed, name));

        if (ignored) {
            achievements.ProgressAchievement(session, "ACH_SelfModIgnoreSeen", 1);
        }
    }
}
