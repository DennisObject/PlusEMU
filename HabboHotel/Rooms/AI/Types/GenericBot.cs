using System.Drawing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Filter;

namespace Plus.HabboHotel.Rooms.AI.Types;

public class GenericBot : BotAi
{
    private readonly int _virtualId;
    private readonly IWordFilterManager _wordFilter;
    private int _actionTimer;
    private int _speechTimer;

    public GenericBot(int virtualId, IWordFilterManager wordFilter)
    {
        _virtualId = virtualId;
        _wordFilter = wordFilter;
    }

    public override void OnSelfEnterRoom()
    {
        var botData = GetBotData();
        if (botData == null) return;

        if (botData.IsTemporary)
        {
            _speechTimer = Random.Shared.Next(1, botData.SpeakingInterval + 1);
            _actionTimer = Random.Shared.Next(1, 15);
        }
    }

    public override void OnSelfLeaveRoom(bool kicked) { }

    public override void OnUserEnterRoom(RoomUser user) { }

    public override void OnUserLeaveRoom(GameClient client) { }

    public override void OnUserSay(RoomUser user, string message) { }

    public override void OnUserShout(RoomUser user, string message) { }

    public override void OnTimerTick()
    {
        var botRoom = GetRoom();
        if (botRoom == null) return;
        var botUser = GetRoomUser();
        if (botUser == null) return;
        var botData = GetBotData();
        if (botData == null) return;

        if (botData == null)
            return;
        if (_speechTimer <= 0)
        {
            if (botData.RandomSpeech.Count > 0)
            {
                if (botData.AutomaticChat == false)
                    return;
                var speech = botData.GetRandomSpeech();
                var @string = _wordFilter.CheckMessage(speech.Message);
                if (@string.Contains("<img src") || @string.Contains("<font ") || @string.Contains("</font>") || @string.Contains("</a>") || @string.Contains("<i>"))
                    @string = "I really shouldn't be using HTML within bot speeches.";
                botUser.Chat(@string, botData.ChatBubble);
            }
            _speechTimer = botData.SpeakingInterval;
        }
        else
            _speechTimer--;
        if (_actionTimer <= 0)
        {
            switch (botData.WalkingMode.ToLower())
            {
                default:
                case "stand":
                    // (8) Why is my life so boring?
                    break;
                case "freeroam":
                    if (botData.ForcedMovement)
                    {
                        if (botUser.Coordinate == botData.TargetCoordinate)
                        {
                            botData.ForcedMovement = false;
                            botData.TargetCoordinate = new();
                            botUser.MoveTo(botData.TargetCoordinate.X, botData.TargetCoordinate.Y);
                        }
                    }
                    else if (botData.ForcedUserTargetMovement > 0)
                    {
                        var target = botRoom.GetRoomUserManager().GetRoomUserByHabbo(botData.ForcedUserTargetMovement);
                        if (target == null)
                        {
                            botData.ForcedUserTargetMovement = 0;
                            botUser.ClearMovement(true);
                        }
                        else
                        {
                            var sq = new Point(target.X, target.Y);
                            if (target.RotBody == 0)
                                sq.Y--;
                            else if (target.RotBody == 2)
                                sq.X++;
                            else if (target.RotBody == 4)
                                sq.Y++;
                            else if (target.RotBody == 6) sq.X--;
                            botUser.MoveTo(sq);
                        }
                    }
                    else if (botData.TargetUser == 0)
                    {
                        if (botRoom.GetGameMap().TryGetRandomWalkableSquare(botData.IsTemporary, out var nextCoord))
                            botUser.MoveTo(nextCoord.X, nextCoord.Y, botData.IsTemporary);
                    }
                    break;
                case "specified_range":
                    break;
            }
            _actionTimer = Random.Shared.Next(5, 15);
        }
        else
            _actionTimer--;
    }
}
