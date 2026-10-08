using System.Globalization;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class BuildHeightCommand : IChatCommand
{
    public string Key => "bh";

    public string Parameters => "%height%";

    public string Description => "Place and move furniture at a fixed height. Type :bh without a height to reset.";

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null) {
            return;
        }

        if (parameters.Length == 0) {
            user.BuildHeight = null;
            session.SendWhisper("Build height reset, furniture stacks normally again.");

            return;
        }

        if (!double.TryParse(parameters[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || !double.IsFinite(height) || height < 0 || height > MagicTileHeight.MaximumHeight) {
            session.SendWhisper($"Please enter a height between 0 and {MagicTileHeight.MaximumHeight.ToString(CultureInfo.InvariantCulture)}.");

            return;
        }

        user.BuildHeight = Math.Round(height, 2);
        session.SendWhisper($"Build height set to {user.BuildHeight.Value.ToString(CultureInfo.InvariantCulture)}. Type :bh to reset.");
    }
}
