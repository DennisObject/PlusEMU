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
        connection.Query<BotSqlRow>("""
            SELECT id, room_id AS RoomId, name, motto, look, x, y, z, rotation, user_id AS UserId,
                   ai_type AS AiType, walk_mode AS WalkMode, automatic_chat AS AutomaticChat,
                   speaking_interval AS SpeakingInterval, mix_sentences AS MixSentences, chat_bubble AS ChatBubble
            FROM bots WHERE room_id = @roomId AND ai_type != 'pet'
            """, new { roomId })
            .Select(row => new BotRow(checked((int)row.Id), row.RoomId, row.Name, row.Motto, row.Look, row.X, row.Y,
                row.Z, row.Rotation, checked((int)row.UserId), row.AiType, row.WalkMode, row.AutomaticChat,
                row.SpeakingInterval, row.MixSentences, row.ChatBubble)).ToArray();

    internal static IEnumerable<string> LoadSpeech(System.Data.IDbConnection connection, int botId) =>
        connection.Query<string>("SELECT text FROM bots_speech WHERE bot_id = @botId", new { botId });

    internal sealed record BotRow(int Id, uint RoomId, string Name, string Motto, string Look, int X, int Y, double Z,
        int Rotation, int UserId, string AiType, string WalkMode, bool AutomaticChat, int SpeakingInterval,
        bool MixSentences, int ChatBubble);

    private sealed class BotSqlRow
    {
        public uint Id { get; set; }
        public uint RoomId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Motto { get; set; } = string.Empty;
        public string Look { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public double Z { get; set; }
        public int Rotation { get; set; }
        public uint UserId { get; set; }
        public string AiType { get; set; } = string.Empty;
        public string WalkMode { get; set; } = string.Empty;
        public bool AutomaticChat { get; set; }
        public int SpeakingInterval { get; set; }
        public bool MixSentences { get; set; }
        public int ChatBubble { get; set; }
    }
}
