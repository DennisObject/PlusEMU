using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

/// <summary>Maintenance workflow over the real gate, wallet lock, game client manager and packet encoding; only the row store is faked.</summary>
public sealed class UserMaintenanceServiceTests
{
    [Theory]
    [InlineData("coins", 1)]
    [InlineData("credits", 1)]
    [InlineData("pixels", 2)]
    [InlineData("duckets", 2)]
    [InlineData("diamonds", 3)]
    [InlineData("gotw", 4)]
    public async Task EveryAliasChangesItsOwnBalanceOnceAndPersistsIt(string alias, int kind)
    {
        var (habbo, client, sent, clients) = Setup();
        var store = new RecordingStore();

        Assert.True(await Service(store, clients).GiveCurrency(7, alias, 5));

        var (currency, value) = Assert.Single(store.Written);
        Assert.Equal(kind, (int)currency + 1);
        Assert.Equal(value, Balance(habbo, currency));
        Assert.Equal(Start(currency) + 5, value);
        Assert.Single(sent);
    }

    [Theory]
    [InlineData("stars")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnknownAliasesHaveNoEffect(string? alias)
    {
        var (_, _, sent, clients) = Setup();
        var store = new RecordingStore();

        Assert.False(await Service(store, clients).GiveCurrency(7, alias!, 5));

        Assert.Empty(store.Written);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task TakeSubtractsTheSignedAmountAndNotifiesWithItsLegacyPositiveAmount()
    {
        var (habbo, _, sent, clients) = Setup();
        var store = new RecordingStore();
        var service = Service(store, clients);

        Assert.True(await service.TakeCurrency(7, "pixels", 5));
        Assert.Equal(15, habbo.Duckets);
        Assert.True(await service.TakeCurrency(7, "pixels", -5));
        Assert.Equal(20, habbo.Duckets);

        Assert.Equal(new uint[] { ServerPacketHeader.HabboActivityPointNotificationComposer, ServerPacketHeader.HabboActivityPointNotificationComposer },
            sent.Select(packet => packet.Header).ToArray());
        Assert.Equal(new[] { 15, 20 }, store.Written.Select(write => write.Value));
    }

    [Fact]
    public async Task PersistenceRunsBeforeTheBalanceAndThePacketChange()
    {
        var (habbo, _, sent, clients) = Setup();
        var seen = new List<(int Credits, int Packets)>();
        var store = new RecordingStore(() => seen.Add((habbo.Credits, sent.Count)));

        Assert.True(await Service(store, clients).GiveCurrency(7, "credits", 5));

        Assert.Equal(new[] { (100, 0) }, seen);
        Assert.Equal(105, habbo.Credits);
        Assert.Equal(1, sent.Count);
    }

    [Fact]
    public async Task ForcedStoreFailureLeavesBalanceAndPacketsUnchanged()
    {
        var (habbo, _, sent, clients) = Setup();
        var store = new RecordingStore { Throw = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store, clients).GiveCurrency(7, "credits", 5));

        Assert.Equal(100, habbo.Credits);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task MissingAccountRowRefusesWithoutAnyEffect()
    {
        var (habbo, _, sent, clients) = Setup();
        var store = new RecordingStore { Missing = true };

        Assert.False(await Service(store, clients).TakeCurrency(7, "diamonds", 5));

        Assert.Equal(20, habbo.Diamonds);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task ClosedWalletDeniesEveryChangeBeforeTheStore()
    {
        var (habbo, _, sent, clients) = Setup();
        typeof(Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(habbo, true);
        var store = new RecordingStore();
        var service = Service(store, clients);

        Assert.False(await service.GiveCurrency(7, "credits", 5));
        Assert.False(await service.SyncCurrency(7, "credits"));
        Assert.False(await service.ReloadCurrency(7, "credits"));

        Assert.Empty(store.Written);
        Assert.Equal(0, store.Reads);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(int.MaxValue, 1)]
    [InlineData(int.MinValue, -1)]
    public async Task ArithmeticOutsideIntRangeIsRejectedWithoutAnyEffect(int start, int amount)
    {
        var (habbo, _, sent, clients) = Setup();
        habbo.Credits = start;
        var store = new RecordingStore();

        Assert.False(await Service(store, clients).GiveCurrency(7, "credits", amount));

        Assert.Equal(start, habbo.Credits);
        Assert.Empty(store.Written);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task SyncPersistsTheCurrentBalanceWithoutAnyPacket()
    {
        var (habbo, _, sent, clients) = Setup();
        var store = new RecordingStore();

        Assert.True(await Service(store, clients).SyncCurrency(7, "gotw"));

        Assert.Equal(new[] { (UserCurrency.Gotw, habbo.GotwPoints) }, store.Written);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task ReloadWithoutARowRefusesAndWithARowPublishesTheLoadedBalance()
    {
        var (habbo, _, sent, clients) = Setup();
        var store = new RecordingStore { Read = null };
        var service = Service(store, clients);

        Assert.False(await service.ReloadCurrency(7, "credits"));
        Assert.Equal(100, habbo.Credits);
        Assert.Empty(sent);

        store.Read = 42;
        Assert.True(await service.ReloadCurrency(7, "credits"));
        Assert.Equal(42, habbo.Credits);
        Assert.Equal(ServerPacketHeader.CreditBalanceComposer, Assert.Single(sent).Header);
    }

    [Fact]
    public async Task ReloadDuckets()
    {
        var (habbo, _, sent, clients) = Setup();
        var store = new RecordingStore { Read = 11 };

        Assert.True(await Service(store, clients).ReloadCurrency(7, "pixels"));

        Assert.Equal(11, habbo.Duckets);
        Assert.Equal(ServerPacketHeader.HabboActivityPointNotificationComposer, Assert.Single(sent).Header);
    }

    [Fact]
    public async Task MottoWithoutARowOrWithoutAClientChangesNothing()
    {
        var (habbo, _, sent, clients) = Setup();
        var store = new RecordingStore { Motto = null };

        Assert.False(await Service(store, clients).ReloadMotto(7));
        Assert.Equal(string.Empty, habbo.Motto);
        Assert.False(await Service(store, new GameClientManager(null!, null!)).ReloadMotto(7));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task MottoOutsideARoomIsAStoredCurrentValueWithNoPacket()
    {
        var (habbo, _, sent, clients) = Setup();
        var store = new RecordingStore { Motto = "fresh" };

        Assert.True(await Service(store, clients).ReloadMotto(7));

        Assert.Equal("fresh", habbo.Motto);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task MottoInARoomPublishesTheAvatarChangeOnlyWhenTheUserIsPresent()
    {
        var (habbo, client, sent, clients) = Setup();
        var room = Room(habbo);
        var store = new RecordingStore { Motto = "in-room" };
        var service = Service(store, clients);

        Assert.False(await service.ReloadMotto(7));
        Assert.Equal("in-room", habbo.Motto);
        Assert.Empty(sent);

        var user = AddRoomUser(room, client);
        Assert.True(await service.ReloadMotto(7));
        Assert.Equal(ServerPacketHeader.UserChangeComposer, Assert.Single(sent).Header);
        Assert.NotNull(user);
    }

    [Fact]
    public async Task AnAccountGateHeldByAnotherLoginDelaysTheChangeUntilReleaseAndCleansUp()
    {
        var (habbo, _, sent, clients) = Setup();
        var gate = new AccountSessionGate(TimeSpan.FromSeconds(20));
        var store = new RecordingStore();
        var service = new UserMaintenanceService(store, gate, clients);
        var held = await gate.EnterAsync(7);
        Task<bool>? change = null;
        try
        {
            change = service.GiveCurrency(7, "credits", 5);
            await Task.Delay(200);
            Assert.False(change.IsCompleted);
            Assert.Empty(store.Written);
        }
        finally
        {
            held.Dispose();
            if (change != null) { try { await change.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception) { } }
        }

        Assert.True(await change!);
        Assert.Equal(105, habbo.Credits);
        Assert.Equal(1, sent.Count);
    }

    [Fact]
    public async Task TakeNotifiesDucketsWithTheRemainingBalanceAndTheLegacyPositiveAmount()
    {
        var (habbo, _, sent, clients) = Setup();

        Assert.True(await Service(new RecordingStore(), clients).TakeCurrency(7, "pixels", 5));

        Assert.Equal(15, habbo.Duckets);
        Assert.Equal(Int32s(15, 5, 0), Assert.Single(sent).Payload);
    }

    [Fact]
    public async Task TakeWithANegativeAmountKeepsTheSignedDeltaAndItsRawNotification()
    {
        var (habbo, _, sent, clients) = Setup();

        Assert.True(await Service(new RecordingStore(), clients).TakeCurrency(7, "pixels", -5));

        Assert.Equal(25, habbo.Duckets);
        Assert.Equal(Int32s(25, -5, 0), Assert.Single(sent).Payload);
    }

    [Fact]
    public async Task ReloadNotifiesDucketsWithTheLoadedBalanceTwice()
    {
        var (_, _, sent, clients) = Setup();

        Assert.True(await Service(new RecordingStore { Read = 11 }, clients).ReloadCurrency(7, "duckets"));

        Assert.Equal(Int32s(11, 11, 0), Assert.Single(sent).Payload);
    }

    [Fact]
    public async Task GiveCreditsSendsTheDecimalBalanceString()
    {
        var (_, _, sent, clients) = Setup();

        Assert.True(await Service(new RecordingStore(), clients).GiveCurrency(7, "coins", 5));

        Assert.Equal(Encode("105.0"), Assert.Single(sent).Payload);
    }

    [Fact]
    public async Task DiamondsAndGotwUseTheirFixedNotificationTypes()
    {
        var (_, _, sent, clients) = Setup();
        var service = Service(new RecordingStore(), clients);

        Assert.True(await service.GiveCurrency(7, "diamonds", 5));
        Assert.True(await service.GiveCurrency(7, "gotw", 5));

        Assert.Equal(new[] { Int32s(25, 0, 5), Int32s(25, 0, 103) }, sent.Select(packet => packet.Payload).ToArray());
    }

    private static byte[] Int32s(params int[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var index = 0; index < values.Length; index++) BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(index * 4), values[index]);
        return bytes;
    }

    // Flash string framing: a big-endian UInt16 length followed by UTF-8 bytes.
    private static byte[] Encode(string value)
    {
        var text = Encoding.UTF8.GetBytes(value);
        var bytes = new byte[2 + text.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, checked((ushort)text.Length));
        text.CopyTo(bytes, 2);
        return bytes;
    }

    private static int Start(UserCurrency currency) => currency switch
    {
        UserCurrency.Credits => 100,
        UserCurrency.Duckets => 20,
        UserCurrency.Diamonds => 20,
        _ => 20,
    };

    private static int Balance(Habbo habbo, UserCurrency currency) => currency switch
    {
        UserCurrency.Credits => habbo.Credits,
        UserCurrency.Duckets => habbo.Duckets,
        UserCurrency.Diamonds => habbo.Diamonds,
        _ => habbo.GotwPoints,
    };

    private static (Habbo Habbo, Plus.Communication.Flash.FlashGameClient Client, List<(uint Header, byte[] Payload)> Sent, IGameClientManager Clients) Setup()
    {
        var habbo = new Habbo { Id = 7, Username = "target", Credits = 100, Duckets = 20, Diamonds = 20, GotwPoints = 20,
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0) };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var clients = new GameClientManager(null!, null!);
        clients.RegisterClient(client, habbo.Id, habbo.Username);
        return (habbo, client, sent, clients);
    }

    private static UserMaintenanceService Service(RecordingStore store, IGameClientManager clients) =>
        new(store, new AccountSessionGate(), clients);

    private static Room Room(Habbo habbo)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9; room.OwnerName = "owner"; room.Type = "private"; room.UsersWithRights = [];
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        habbo.CurrentRoom = room;
        return room;
    }

    private static RoomUser AddRoomUser(Room room, Plus.Communication.Flash.FlashGameClient client)
    {
        var user = new RoomUser(7, room.Id, 1, room);
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(user, client);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomUserManager())!;
        users.TryAdd(1, user);
        return user;
    }

    private sealed class RecordingStore(Action? beforeWrite = null) : IUserMaintenanceStore
    {
        public bool Throw { get; init; }
        public bool Missing { get; init; }
        public int? Read { get; set; }
        public string? Motto { get; set; }
        public int Reads { get; private set; }
        public List<(UserCurrency Currency, int Value)> Written { get; } = [];
        public int? ReadCurrency(int userId, UserCurrency currency) { Reads++; return Read; }
        public bool TryWriteCurrency(int userId, UserCurrency currency, int value)
        {
            beforeWrite?.Invoke();
            if (Throw) throw new InvalidOperationException("forced failure");
            if (Missing) return false;
            Written.Add((currency, value));
            return true;
        }
        public string? ReadMotto(int userId) => Motto;
    }
}
