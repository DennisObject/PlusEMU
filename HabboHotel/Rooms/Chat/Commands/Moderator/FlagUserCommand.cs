using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class FlagUserCommand(IUserPersistenceService persistence) : ITargetChatCommand
{
    public string Key => "flaguser";

    public string Parameters => "%username%";

    public string Description => "Forces the specified user to change their name.";

    public bool MustBeInSameRoom => false;

    public Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        if (!session.GetHabbo().Access.Outranks(target.Access))
        {
            session.SendWhisper("You are not allowed to flag that user.");

            return Task.CompletedTask;
        }

        persistence.SetProfileValue(target.Id, "last_change", null);
        target.LastNameChangedAt = null;
        target.ChangingName = true;
        target.Client?.SendNotification("Please be aware that if your username is deemed as inappropriate, you will be banned without question.\r\rAlso note that Staff will NOT allow you to change your username again should you have an issue with what you have chosen.\r\rClose this window and click yourself to begin choosing a new username!");
        target.Client?.Send(new UserObjectComposer(UserObjectSnapshot.Capture(target)));

        return Task.CompletedTask;
    }
}
