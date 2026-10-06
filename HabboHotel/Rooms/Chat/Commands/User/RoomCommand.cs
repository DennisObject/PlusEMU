using System.Text;
using Dapper;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class RoomCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Key => "room";

    public string Parameters => "push/pull/enables/respect";

    public string Description => "Gives you the ability to enable or disable basic room commands.";

    public RoomCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!parameters.Any())
        {
            session.SendWhisper("Oops, you must choose a room option to disable.");

            return;
        }

        if (!room.CheckRights(session, true))
        {
            session.SendWhisper("Oops, only the room owner or staff can use this command.");

            return;
        }

        var option = parameters[0];
        void Persist(string column, bool value)
        {
            using var connection = _database.Connection();
            connection.Execute($"UPDATE rooms SET `{column}`=@value WHERE id=@roomId LIMIT 1", new
            {
                value,
                roomId = room.Id
            });
        }

        switch (option)
        {
            case "list":
                {
                    var list = new StringBuilder("");
                    list.AppendLine("Room Command List");
                    list.AppendLine("-------------------------");
                    list.AppendLine($"Pet Morphs: {(room.PetMorphsAllowed ? "enabled" : "disabled")}");
                    list.AppendLine($"Pull: {(room.PullEnabled ? "enabled" : "disabled")}");
                    list.AppendLine($"Push: {(room.PushEnabled ? "enabled" : "disabled")}");
                    list.AppendLine($"Super Pull: {(room.SuperPullEnabled ? "enabled" : "disabled")}");
                    list.AppendLine($"Super Push: {(room.SuperPushEnabled ? "enabled" : "disabled")}");
                    list.AppendLine($"Respect: {(room.RespectNotificationsEnabled ? "enabled" : "disabled")}");
                    list.AppendLine($"Enables: {(room.EnablesEnabled ? "enabled" : "disabled")}");
                    session.SendNotification(list.ToString());
                    break;
                }
            case "push":
                {
                    room.PushEnabled = !room.PushEnabled;
                    Persist("push_enabled", room.PushEnabled);
                    session.SendWhisper($"Push mode is now {(room.PushEnabled ? "enabled!" : "disabled!")}");
                    break;
                }
            case "spush":
                {
                    room.SuperPushEnabled = !room.SuperPushEnabled;
                    Persist("spush_enabled", room.SuperPushEnabled);
                    session.SendWhisper($"Super Push mode is now {(room.SuperPushEnabled ? "enabled!" : "disabled!")}");
                    break;
                }
            case "spull":
                {
                    room.SuperPullEnabled = !room.SuperPullEnabled;
                    Persist("spull_enabled", room.SuperPullEnabled);
                    session.SendWhisper($"Super Pull mode is now {(room.SuperPullEnabled ? "enabled!" : "disabled!")}");
                    break;
                }
            case "pull":
                {
                    room.PullEnabled = !room.PullEnabled;
                    Persist("pull_enabled", room.PullEnabled);
                    session.SendWhisper($"Pull mode is now {(room.PullEnabled ? "enabled!" : "disabled!")}");
                    break;
                }
            case "enable":
            case "enables":
                {
                    room.EnablesEnabled = !room.EnablesEnabled;
                    Persist("enables_enabled", room.EnablesEnabled);
                    session.SendWhisper($"Enables mode set to {(room.EnablesEnabled ? "enabled!" : "disabled!")}");
                    break;
                }
            case "respect":
                {
                    room.RespectNotificationsEnabled = !room.RespectNotificationsEnabled;
                    Persist("respect_notifications_enabled", room.RespectNotificationsEnabled);
                    session.SendWhisper($"Respect notifications mode set to {(room.RespectNotificationsEnabled ? "enabled!" : "disabled!")}");
                    break;
                }
            case "pets":
            case "morphs":
                {
                    room.PetMorphsAllowed = !room.PetMorphsAllowed;
                    Persist("pet_morphs_allowed", room.PetMorphsAllowed);
                    session.SendWhisper($"Human pet morphs notifications mode set to {(room.PetMorphsAllowed ? "enabled!" : "disabled!")}");

                    if (!room.PetMorphsAllowed)
                    {
                        foreach (var user in room.GetRoomUserManager().GetRoomUsers())
                        {
                            if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null)
                            {
                                continue;
                            }

                            user.GetClient().SendWhisper("The room owner has disabled the ability to use a pet morph in this room.");

                            if (user.GetClient().GetHabbo().PetId > 0)
                            {
                                //Tell the user what is going on.
                                user.GetClient().SendWhisper("Oops, the room owner has just disabled pet-morphs, un-morphing you.");

                                //Change the users Pet Id.
                                user.GetClient().GetHabbo().PetId = 0;

                                //Quickly remove the old user instance.
                                room.SendPacket(new UserRemoveComposer(user.VirtualId));

                                //Add the new one, they won't even notice a thing!!11 8-)
                                room.SendUser(user);
                            }
                        }
                    }

                    break;
                }
        }
    }
}
