using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Core.Language;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Moderation;

public interface IModeratorUserInfoService
{
    void Send(GameClient session, int userId);
}

public sealed class ModeratorUserInfoService(IModerationUserStore users, IGameClientManager clients, ILanguageManager language, TimeProvider clock) : IModeratorUserInfoService
{
    public void Send(GameClient session, int userId)
    {
        var user = users.Find(userId);
        if (user == null)
        {
            session.SendNotification(language.TryGetValue("user.not_found"));
            return;
        }
        session.Send(new ModeratorUserInfoComposer(user, clients.GetClientByUserId(userId)?.GetHabbo() != null, clock.GetUtcNow()));
    }
}
