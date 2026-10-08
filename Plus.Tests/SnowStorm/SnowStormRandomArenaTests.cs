using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Xunit;
using static Plus.Tests.SnowStorm.SnowStormTestSupport;

namespace Plus.Tests.SnowStorm;

public class SnowStormRandomArenaTests
{
    private readonly TestClock _clock = new();
    private readonly Store _store = new();

    [Fact]
    public void EveryNewLobbyPicksAnOfferedArenaUniformlyAtRandom()
    {
        var manager = Manager(_store, _clock);
        manager.Random = new Random(1234);
        var player = Player(1, "Ann");
        var counts = new Dictionary<int, int>();

        for (var lobby = 0; lobby < 3000; lobby++) {
            player.Sent.Clear();
            manager.QuickJoin(player.Client);
            manager.Tick();
            var fieldType = FieldType(player, ServerPacketHeader.Game2GameCreatedComposer);
            counts[fieldType] = counts.GetValueOrDefault(fieldType) + 1;
            manager.LeaveLobby(player.Client);
            manager.Tick();
        }

        Assert.Equal([8, 9, 11], counts.Keys.Order());
        Assert.All(counts.Values, count => Assert.InRange(count, 900, 1100));

        // Only offered arenas are picked; unknown field types in the setting are skipped.
        var restricted = Manager(_store, _clock, null, ("gamecenter.snowwar.arenas", "9,404"));
        var bo = Player(2, "Bo");
        restricted.QuickJoin(bo.Client);
        restricted.Tick();
        Assert.Equal(9, FieldType(bo, ServerPacketHeader.Game2GameCreatedComposer));
    }

    [Fact]
    public void GamesAndRematchesPlayTheArenaTheirLobbyShows()
    {
        var manager = Manager(_store, _clock, null, ("gamecenter.snowwar.game.length.seconds", "10"));
        manager.Random = new Random(7);
        var ann = Player(1, "Ann");
        var bo = Player(2, "Bo");
        manager.QuickJoin(ann.Client);
        manager.QuickJoin(bo.Client);
        manager.Tick();
        var shown = FieldType(bo, ServerPacketHeader.Game2GameLongDataComposer);
        Assert.Equal(shown, FieldType(ann, ServerPacketHeader.Game2GameCreatedComposer));
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        Assert.Equal(shown, FieldType(ann, ServerPacketHeader.Game2GameStartedComposer));
        Assert.Equal(shown, EnterArenaFieldType(ann));

        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();

        for (var turn = 0; turn < 120; turn++) {
            _clock.Advance(TimeSpan.FromMilliseconds(150));
            manager.Tick();
        }

        manager.PlayAgain(ann.Client);
        manager.PlayAgain(bo.Client);
        manager.Tick();
        ann.Sent.Clear();
        _clock.Advance(TimeSpan.FromSeconds(30));
        manager.Tick();
        var rematch = FieldType(ann, ServerPacketHeader.Game2GameCreatedComposer);
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        Assert.Equal(rematch, FieldType(ann, ServerPacketHeader.Game2GameStartedComposer));
        Assert.Equal(rematch, EnterArenaFieldType(ann));
    }

    // GameLobbyData: game id, level name, game type, then the field type.
    private static int FieldType((GameClient Client, List<(uint Header, byte[] Payload)> Sent, Habbo Habbo) player, uint header)
    {
        var packet = Read(player.Sent.Last(sent => sent.Header == header).Payload);
        packet.ReadInt();
        packet.ReadString();
        packet.ReadInt();

        return packet.ReadInt();
    }

    private static int EnterArenaFieldType((GameClient Client, List<(uint Header, byte[] Payload)> Sent, Habbo Habbo) player)
    {
        var packet = Read(player.Sent.Last(sent => sent.Header == ServerPacketHeader.Game2EnterArenaComposer).Payload);
        packet.ReadInt();

        return packet.ReadInt();
    }
}
