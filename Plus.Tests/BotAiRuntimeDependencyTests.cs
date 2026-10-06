using System.Reflection;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Bots;
using Plus.HabboHotel;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Responses;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Rooms.AI.Types;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Rooms.Chat.Pets.Commands;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void GenericAndBartenderUseInjectedSpeechFilterAndResponseWithoutGlobals()
    {
        var human = LegacyRider();
        var speeches = new List<RandomSpeech> { new("raw", 91) };
        var generic = Bot(91, "generic", speeches);
        var filter = new RecordingFilter();
        var genericAi = new GenericBot(generic.VirtualId, filter);
        genericAi.Init(91, generic.VirtualId, RoomId, generic, _room);
        typeof(GenericBot).GetField("_speechTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(genericAi, 0);
        _gameField.SetValue(null, Proxy<IGame>((method, _) => throw new InvalidOperationException(method)));

        genericAi.OnTimerTick();
        Assert.Equal(new[] { "raw" }, filter.Messages);
        Assert.Contains(ServerPacketHeader.ChatComposer, _client.Sent);

        _client.Sent.Clear();
        var bartender = Bot(92, "bartender", [new("raw", 92)]);
        var responses = new RecordingBots();
        var bartenderAi = new BotAiFactory(new RecordingLocale(), new RecordingCommands(0), filter, responses)
            .Create(BotAiType.Bartender, bartender.VirtualId);
        bartenderAi.Init(92, bartender.VirtualId, RoomId, bartender, _room);
        bartenderAi.OnUserSay(human, "drink");
        Assert.Equal(new[] { "drink" }, responses.Messages);
        Assert.Contains(ServerPacketHeader.ChatComposer, _client.Sent);

        typeof(BartenderBot).GetField("_speechTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bartenderAi, 0);
        bartenderAi.OnTimerTick();
        Assert.Equal(new[] { "raw", "raw" }, filter.Messages);
    }

    [Fact]
    public void PetBotUsesInjectedValidAndMissingCommandsAndLocaleWithoutGlobals()
    {
        var owner = LegacyRider();
        var petUser = LegacyHorse(2, 1);
        petUser.PetData.ExperienceLevels = Enumerable.Range(1, 20).Select(i => i * 100).ToArray();
        petUser.PetData.Experience = 1600;
        var locale = new RecordingLocale();
        var valid = new PetBot(petUser.VirtualId, locale, new RecordingCommands(3));
        valid.Init(50, petUser.VirtualId, RoomId, petUser, _room);
        _gameField.SetValue(null, Proxy<IGame>((method, _) => throw new InvalidOperationException(method)));

        valid.OnUserSay(owner, "horse sit");
        Assert.Contains("sit", petUser.Statusses.Keys);
        Assert.Contains(ServerPacketHeader.AddExperiencePointsComposer, _client.Sent);

        _client.Sent.Clear();
        petUser.Statusses.Clear();
        var missing = new PetBot(petUser.VirtualId, locale, new RecordingCommands(8));
        missing.Init(50, petUser.VirtualId, RoomId, petUser, _room);
        missing.OnUserSay(owner, "horse missing");
        Assert.Contains("pet.unknowncommand", locale.Keys);
        Assert.Contains(ServerPacketHeader.ChatComposer, _client.Sent);
    }

    private RoomUser Bot(int id, string type, List<RandomSpeech> speeches)
    {
        var data = new RoomBot(id, RoomId, type, "stand", type, "", "hd-180-1", 1, 1, 0, 0,
            0, 0, 0, 0, ref speeches, "M", 0, 7, true, 60, false, 0);
        var user = new RoomUser(0, RoomId, id, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused)
        {
            InternalRoomId = id,
            BotData = data,
            X = 1,
            Y = 1
        };
        Assert.True(LegacyUsers().TryAdd(id, user));
        return user;
    }

    private sealed class RecordingFilter : IWordFilterManager
    {
        public List<string> Messages { get; } = [];
        public void Init() { }
        public string CheckMessage(string message) { Messages.Add(message); return $"filtered:{message}"; }
        public bool CheckBannedWords(string message) => false;
        public bool IsFiltered(string message) => false;
    }

    private sealed class RecordingBots : IBotManager
    {
        public List<string> Messages { get; } = [];
        public Task Init() => Task.CompletedTask;
        public BotResponse? GetResponse(BotAiType type, string message)
        {
            Messages.Add(message);
            return new("bartender", message, "served", "say", "");
        }
    }

    private sealed class RecordingCommands(int result) : IPetCommandManager
    {
        public void Init() { }
        public int TryInvoke(string input) => result;
    }

    private sealed class RecordingLocale : IPetLocale
    {
        public List<string> Keys { get; } = [];
        public void Init() { }
        public string[] GetValue(string key) { Keys.Add(key); return [key]; }
    }
}
