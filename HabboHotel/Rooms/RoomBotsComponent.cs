using Dapper;
using Plus.Database;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms;

public sealed class RoomBotsComponent(IDatabase database) : IRoomComponent
{
    private Room _room = null!;
    public void Initiate(Room room) => _room = room;
    public void Initiated()
    {
        using var connection = database.Connection();
        foreach (var bot in connection.Query<BotRow>("""
            SELECT id, room_id AS RoomId, name, motto, look, x, y, z, rotation, user_id AS UserId,
                   ai_type AS AiType, walk_mode AS WalkMode, automatic_chat AS AutomaticChat,
                   speaking_interval AS SpeakingInterval, mix_sentences AS MixSentences, chat_bubble AS ChatBubble
            FROM bots WHERE room_id = @roomId AND ai_type != 'pet'
            """, new { roomId = _room.Id }))
        {
            var speeches = connection.Query<string>("SELECT text FROM bots_speech WHERE bot_id = @botId", new { botId = bot.Id })
                .Select(text => new RandomSpeech(text, bot.Id)).ToList();
            _room.GetRoomUserManager().DeployBot(new(bot.Id, bot.RoomId, bot.AiType, bot.WalkMode, bot.Name,
                bot.Motto, bot.Look, bot.X, bot.Y, bot.Z, bot.Rotation, 0, 0, 0, 0, ref speeches, "M", 0,
                bot.UserId, bot.AutomaticChat, bot.SpeakingInterval, ConvertExtensions.EnumToBool(bot.MixSentences), bot.ChatBubble), null);
        }
    }

    private sealed record BotRow(int Id, uint RoomId, string Name, string Motto, string Look, int X, int Y, int Z,
        int Rotation, int UserId, string AiType, string WalkMode, bool AutomaticChat, int SpeakingInterval,
        string MixSentences, int ChatBubble);
}
