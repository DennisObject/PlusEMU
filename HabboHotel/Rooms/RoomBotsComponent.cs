using Dapper;
using Plus.Database;
using Plus.HabboHotel.Rooms.AI.Speech;

namespace Plus.HabboHotel.Rooms;

public sealed class RoomBotsComponent(IDatabase database) : IRoomComponent
{
    public int Order => 300;
    private Room _room = null!;
    public void Initiate(Room room) => _room = room;
    public void Initiated()
    {
        using var connection = database.Connection();
        foreach (var bot in Load(connection, _room.Id))
        {
            var speeches = LoadSpeech(connection, bot.Id).Select(text => new RandomSpeech(text, bot.Id)).ToList();
            _room.GetRoomUserManager().DeployBot(new(bot.Id, bot.RoomId, bot.AiType, bot.WalkMode, bot.Name,
                bot.Motto, bot.Look, bot.X, bot.Y, Convert.ToInt32(bot.Z), bot.Rotation, 0, 0, 0, 0, ref speeches, "M", 0,
                bot.UserId, bot.AutomaticChat, bot.SpeakingInterval, bot.MixSentences, bot.ChatBubble), null);
        }
    }

    internal static IEnumerable<BotRow> Load(System.Data.IDbConnection connection, uint roomId) =>
        connection.Query<BotRow>("""
            SELECT id, room_id AS RoomId, name, motto, look, x, y, z, rotation, user_id AS UserId,
                   ai_type AS AiType, walk_mode AS WalkMode, automatic_chat AS AutomaticChat,
                   speaking_interval AS SpeakingInterval, mix_sentences AS MixSentences, chat_bubble AS ChatBubble
            FROM bots WHERE room_id = @roomId AND ai_type != 'pet'
            """, new { roomId });

    internal static IEnumerable<string> LoadSpeech(System.Data.IDbConnection connection, int botId) =>
        connection.Query<string>("SELECT text FROM bots_speech WHERE bot_id = @botId", new { botId });

    internal sealed record BotRow(int Id, uint RoomId, string Name, string Motto, string Look, int X, int Y, double Z,
        int Rotation, int UserId, string AiType, string WalkMode, bool AutomaticChat, int SpeakingInterval,
        bool MixSentences, int ChatBubble);
}
