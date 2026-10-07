using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;

namespace Plus.Tests.SnowStorm;

internal static class SnowStormTestSupport
{
    public static string ArenaDirectory => HabbiconPacketTests.Repo("Resources/SnowStorm");

    public static SnowStormArenas Arenas() => new(ArenaDirectory, NullLogger<SnowStormArenas>.Instance);

    public static (GameClient Client, List<(uint Header, byte[] Payload)> Sent, Habbo Habbo) Player(int id, string name)
    {
        var habbo = new Habbo { Id = id, Username = name, Look = "hd-180-1.ch-210-66", Gender = "m" };
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        return (client, sent, habbo);
    }

    public static FlashIncomingPacket Read(byte[] payload) => new() { Buffer = payload };

    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    internal sealed class Settings(params (string Key, string Value)[] values) : ISettingsManager
    {
        private readonly Dictionary<string, string> _values = values.ToDictionary(value => value.Key, value => value.Value);

        public string TryGetValue(string value) => _values.GetValueOrDefault(value, "0");

        public string? GetOptionalValue(string key) => _values.GetValueOrDefault(key);

        public Task Reload() => Task.CompletedTask;
    }

    internal sealed class Filter : IWordFilterManager
    {
        public void Init() { }

        public string CheckMessage(string message) => message.Replace("bad", "bobba");

        public bool CheckBannedWords(string message) => false;

        public bool IsFiltered(string message) => false;
    }

    internal sealed class Store : ISnowStormStore
    {
        public Dictionary<int, SnowStormAccount> Accounts { get; } = [];
        public List<int> Consumed { get; } = [];
        public List<(DateOnly Week, int UserId, int Score)> Recorded { get; } = [];
        public List<SnowStormLeaderboardRequest> Requests { get; } = [];

        public SnowStormAccount GetAccount(int userId, DateOnly today) => Accounts.GetValueOrDefault(userId) ?? new SnowStormAccount(0, 0, 0);

        public bool TryConsumeGame(int userId, DateOnly today, int freeGamesPerDay)
        {
            if (GetAccount(userId, today).GamesLeft(freeGamesPerDay) == 0) {
                return false;
            }

            Consumed.Add(userId);

            return true;
        }

        public IReadOnlyDictionary<int, int> GetTotalScores(IReadOnlyCollection<int> userIds) => userIds.ToDictionary(id => id, id => 120);

        public void RecordScores(DateOnly weekStart, IReadOnlyList<(int UserId, int Score)> scores) =>
            Recorded.AddRange(scores.Select(score => (weekStart, score.UserId, score.Score)));

        public SnowStormLeaderboardPage LoadLeaderboard(SnowStormLeaderboardRequest request, DateTimeOffset now)
        {
            Requests.Add(request);

            return new([new SnowStormLeaderboardEntry(1, 50, 1, "Ann", "look", "f")], 1, 0,
                request.Weekly ? new SnowStormLeaderboardWeek(2026, 41, 2, request.WeekOffset, 100) : null, request.Group ? 7 : 0);
        }

        public ImmutableArray<SnowStormTokenOffer> GetOffers() => [new(1, "GET_SNOWWAR_TOKENS", 10, 0, 0, 10)];

        public SnowStormTokenOffer? Purchase(Habbo habbo, int offerId) => null;
    }
}
