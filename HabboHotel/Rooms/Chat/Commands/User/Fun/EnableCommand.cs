using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Games.Teams;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User.Fun;

internal class EnableCommand : IChatCommand
{
    public string Key => "enable";

    public string Parameters => "%EffectId%";

    public string Description => "Gives you the ability to set an effect on your user!";

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (parameters.Length == 0) {
            session.SendWhisper("You must enter an effect ID!");

            return;
        }

        if (!room.EnablesEnabled && !session.GetHabbo().Access.Can(PermissionKeys.ModerationTool)) {
            session.SendWhisper("Oops, it appears that the room owner has disabled the ability to use the enable command in here.");

            return;
        }

        var thisUser = session.GetHabbo().CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Username);

        if (thisUser == null) {
            return;
        }

        if (thisUser.RidingHorse) {
            session.SendWhisper("You cannot enable effects whilst riding a horse!");

            return;
        }

        if (thisUser.Team != Team.None) {
            return;
        }

        if (thisUser.IsLying) {
            return;
        }

        var effectId = 0;

        if (!int.TryParse(parameters[0], out effectId)) {
            return;
        }

        if (effectId > int.MaxValue || effectId < int.MinValue) {
            return;
        }

        if ((effectId == 102 || effectId == 187) && !session.GetHabbo().Access.Can(PermissionKeys.ModerationTool)) {
            session.SendWhisper("Sorry, only staff members can use this effects.");

            return;
        }

        if (effectId == 178 && session.GetHabbo().Access.Limit("limit.staff_effect", 0) < 178) {
            session.SendWhisper("Sorry, only Gold VIP and Events Staff members can use this effect.");

            return;
        }

        if (session.GetHabbo().Effects is not { } effects) {
            return;
        }

        effects.ApplyEffect(effectId);
    }
}
