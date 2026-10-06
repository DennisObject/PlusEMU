using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Handshake;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.Core;
using Plus.Core.FigureData;
using Plus.Core.Language;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Catalog.Clothing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rewards;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.UserData;
using Xunit;
using Xunit.Abstractions;

namespace Plus.Tests;

public sealed class FoundationRuntimeDatabaseFactAttribute : FactAttribute
{
    public FoundationRuntimeDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FOUNDATION_RUNTIME_DATABASE")))
            Skip = "Opt-in isolated foundation login and room-entry MariaDB smoke.";
    }
}

[CollectionDefinition("Foundation runtime database", DisableParallelization = true)]
public sealed class FoundationRuntimeDatabaseCollection;

[Collection("Foundation runtime database")]
public sealed class FoundationRuntimeSmokeTests(ITestOutputHelper output)
{
    [FoundationRuntimeDatabaseFact]
    public async Task PublicLoginAndRoomAdmissionUseShippedDatabaseShapes()
    {
        await FoundationRuntimeDatabase.Run(async (database, connection) =>
        {
            Seed(connection);
            using var runtime = new Runtime(database);
            var services = runtime.Services;
            output.WriteLine("Starting the real services used by login and room initialization.");
            await services.GetRequiredService<ISettingsManager>().Reload();
            await services.GetRequiredService<ILanguageManager>().Reload();
            foreach (var type in new[] { typeof(ItemDataManager), typeof(FigureDataManager), typeof(RoomManager),
                         typeof(AccessControl), typeof(ModerationManager), typeof(AchievementManager), typeof(BadgeManager),
                         typeof(PetLocale), typeof(ChatStyleManager), typeof(ClothingManager), typeof(RewardManager) })
                await ((IStartable)services.GetRequiredService(type)).Start();

            output.WriteLine("Login tasks: " + string.Join(", ", services.GetServices<IUserDataLoadingTask>().Select(task => task.GetType().Name)));
            var (client, sent) = Client(services.GetRequiredService<ILogger<GameClient>>());
            var rooms = services.GetRequiredService<IRoomManager>();
            try
            {
                var ticket = await services.GetRequiredService<ISsoTicketStore>().Issue(7);
                output.WriteLine("Calling the public SSO packet handler with a real single-use ticket.");
                await services.GetRequiredService<SSOTicketEvent>().Parse(client, HabbiconTestSupport.Incoming(ticket.Value));
                Assert.True(client.IsAuthenticated);
                var habbo = Assert.IsType<Habbo>(client.GetHabbo());
                Assert.Same(client, services.GetRequiredService<IGameClientManager>().GetClientByUserId(7));
                Assert.Null(await services.GetRequiredService<ISsoTicketStore>().Consume(ticket.Value));
                Assert.Contains(sent, packet => packet.Header == ServerPacketHeader.AuthenticationOkComposer);
                Assert.Equal("smoke_owner", habbo.Username);
                Assert.Equal(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), habbo.AccountCreatedAt);
                Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), habbo.LastOnlineAt);
                Assert.Null(habbo.LastNameChangedAt);
                Assert.Equal(42u, habbo.HomeRoom);
                Assert.True(habbo.AllowMimic);
                Assert.True(habbo.DisableForcedEffects);
                Assert.Equal(123, habbo.HabboStats.AchievementPoints);
                Assert.NotNull(habbo.Messenger);
                Assert.NotNull(habbo.IgnoresComponent);
                Assert.Equal(10, Assert.Single(habbo.Inventory.Bots.Bots).Key);
                Assert.Equal(11, Assert.Single(habbo.Inventory.Pets.Pets).Key);
                var inventoryPet = habbo.Inventory.Pets.Pets[11];
                Assert.Equal("smoke_owner", inventoryPet.OwnerName);
                var createdAt = new DateTimeOffset(2017, 2, 19, 3, 13, 54, TimeSpan.Zero).AddTicks(1234560);
                Assert.Equal(createdAt, inventoryPet.CreatedAt);
                Assert.Equal(TimeSpan.Zero, inventoryPet.CreatedAt!.Value.Offset);
                Assert.Equal(321, Assert.Single(habbo.Clothing.GetClothingParts).PartId);
                Assert.Equal(2, habbo.Effects.GetAllEffects.Count);
                var effect = Assert.Single(habbo.Effects.GetAllEffects, row => row.SpriteId == 1);
                Assert.Equal((1, 3600d, false, 2), (effect.SpriteId, effect.Duration, effect.Activated, effect.Quantity));
                Assert.Null(effect.ActivatedAt);
                var activated = Assert.Single(habbo.Effects.GetAllEffects, row => row.SpriteId == 2);
                Assert.True(activated.Activated);
                Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), activated.ActivatedAt);
                Assert.Contains(sent, packet => packet.Header == ServerPacketHeader.AvatarEffectsComposer);
                Assert.Contains(sent, packet => packet.Header == ServerPacketHeader.FigureSetIdsComposer);

                output.WriteLine("Loading and initializing the room through Habbo.PrepareRoom.");
                habbo.PrepareRoom(42, "");
                var room = Assert.IsType<Room>(habbo.CurrentRoom);
                Assert.True(rooms.TryGetRoom(42, out var loaded));
                Assert.Same(room, loaded);
                Assert.Contains(room.Components, component => component is RoomRuntimeComponent);
                Assert.Contains(room.Components, component => component is RoomDataComponent);
                Assert.True(room.AllowPets);
                Assert.Contains(sent, packet => packet.Header == ServerPacketHeader.RoomReadyComposer);
                Assert.True(room.GetRoomUserManager().TryGetBot(12, out var bot));
                Assert.Equal("room bot", bot!.BotData.Name);
                Assert.True(bot.BotData.AutomaticChat);
                Assert.True(bot.BotData.MixSentences);
                var roomPet = Assert.Single(room.GetRoomUserManager().GetPets());
                Assert.Equal(13, roomPet.PetId);
                Assert.Equal(createdAt, roomPet.CreatedAt);
                Assert.Equal(TimeSpan.Zero, roomPet.CreatedAt!.Value.Offset);
                Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_roomvisits WHERE user_id=7 AND room_id=42"));

                output.WriteLine("Admitting the avatar through the public room-entry packet handler.");
                await services.GetRequiredService<GetRoomEntryDataEvent>().Parse(client, HabbiconTestSupport.Incoming());
                Assert.Same(room, habbo.CurrentRoom);
                var avatar = Assert.IsType<RoomUser>(room.GetRoomUserManager().GetRoomUserByHabbo(7));
                Assert.Equal((room.Model.DoorX, room.Model.DoorY), (avatar.X, avatar.Y));
                Assert.Contains(sent, packet => packet.Header == ServerPacketHeader.RoomEntryInfoComposer);
                Assert.Equal(1, room.UserCount);

                output.WriteLine("Reading DATETIME boundaries through moderation, housekeeping and live trading locks.");
                var moderation = services.GetRequiredService<IModerationUserStore>();
                var housekeeping = services.GetRequiredService<IHousekeepingUserStore>();
                Assert.Equal(habbo.AccountCreatedAt, moderation.Find(7)!.AccountCreatedAt);
                Assert.Equal(habbo.LastOnlineAt, moderation.Find("smoke_owner")!.LastOnlineAt);
                Assert.Equal(habbo.LastOnlineAt, housekeeping.Find(7)!.LastOnlineAt);
                var locks = services.GetRequiredService<ITradingLockService>();
                var expires = locks.Set(7, TimeSpan.FromMinutes(5));
                Assert.True(locks.IsLocked(habbo));
                Assert.Equal(expires, habbo.TradingLockExpiresAt);
                Assert.Equal(expires, moderation.Find(7)!.TradingLockExpiresAt);
                Assert.Equal(expires, housekeeping.Find("smoke_owner")!.TradingLockExpiresAt);
                locks.Clear(7);
                Assert.False(locks.IsLocked(habbo));
                Assert.Null(moderation.Find(7)!.TradingLockExpiresAt);
                Assert.Null(housekeeping.Find(7)!.TradingLockExpiresAt);
            }
            finally
            {
                try { client.OnDisconnected(); }
                finally { if (rooms.TryGetRoom(42, out _)) rooms.UnloadRoom(42); }
            }
            Assert.False(client.IsAuthenticated);
            Assert.Null(services.GetRequiredService<IGameClientManager>().GetClientByUserId(7));
            Assert.Equal(0, rooms.Count);
            Assert.Empty(runtime.Errors);
        });
    }

    private static void Seed(MySqlConnection connection) => connection.Execute("""
        INSERT INTO users (id, username, auth_ticket, look, motto, account_created, last_online) VALUES
            (7, 'smoke_owner', '', 'hd-180-1.ch-210-66.lg-270-82', 'smoke', '2020-01-02 03:04:05', '2026-01-02 03:04:05');
        INSERT INTO users_settings (user_id, home_room, allow_mimic, disable_forced_effects) VALUES (7, 42, TRUE, TRUE);
        INSERT INTO user_statistics (id, AchievementScore) VALUES (7, 123);
        INSERT INTO user_info (user_id) VALUES (7);
        INSERT INTO room_models (id, door_x, door_y, door_z, door_dir, heightmap, public_items)
            VALUES ('smoke', 0, 1, 0, 2, '000\r000\r000', '');
        INSERT INTO rooms (id, owner, caption, model_name, allow_pets) VALUES (42, '7', 'Smoke room', 'smoke', TRUE);
        INSERT INTO bots (id, user_id, room_id, ai_type, name, motto, look, x, y, z, automatic_chat, mix_sentences) VALUES
            (10, 7, 0, 'generic', 'inventory bot', '', 'hd-180-1', 0, 0, 0, 'false', FALSE),
            (11, 7, 0, 'pet', 'inventory pet', '', '', 0, 0, 0, 'false', FALSE),
            (12, 7, 42, 'generic', 'room bot', '', 'hd-180-1', 1, 1, 0, 'true', TRUE),
            (13, 7, 42, 'pet', 'room pet', '', '', 2, 1, 0, 'false', FALSE);
        INSERT INTO bots_petdata (id, type, race, color, createstamp) VALUES
            (11, 0, '0', 'ffffff', '2017-02-19 03:13:54.123456'), (13, 0, '0', 'ffffff', '2017-02-19 03:13:54.123456');
        INSERT INTO bots_speech (bot_id, text) VALUES (12, 'hello');
        INSERT INTO user_clothing (user_id, part_id, part) VALUES (7, '321', 'smoke');
        INSERT INTO user_effects (user_id, effect_id, total_duration, is_activated, activated_stamp, quantity)
            VALUES (7, 1, 3600, FALSE, NULL, 2), (7, 2, 3600, TRUE, '2026-01-02 03:04:05', 1);
        """);

    private static (FlashGameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(ILogger<GameClient> logger)
    {
        var sent = new List<(uint, byte[])>();
        var headers = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (uint)field.GetRawConstantValue()!).Where(id => id > 0).ToDictionary(id => id, id => id);
        return (new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), logger)
        {
            Id = Guid.NewGuid(),
            Revision = new Revision { InternalIdToOutgoingIdMapping = headers },
            SendCallback = args =>
            {
                var bytes = args.MemoryBuffer.ToArray();
                sent.Add((BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));
                return false;
            }
        }, sent);
    }

    private sealed class Runtime : IDisposable
    {
        private readonly Dictionary<FieldInfo, object?> _globals = new();
        private readonly bool _matchUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        private readonly ErrorLogs _logs = new();
        public ServiceProvider Services { get; } = null!;
        public ConcurrentQueue<string> Errors => _logs.Errors;

        public Runtime(IDatabase database)
        {
            foreach (var type in new[] { typeof(PlusEnvironment), typeof(ExceptionLogger) })
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                             .Where(field => !field.IsInitOnly && !field.IsLiteral))
                    _globals[field] = field.GetValue(null);
            var serviceCollection = new ServiceCollection();
            var defaults = typeof(Program).GetField("_defaultTypes", BindingFlags.Static | BindingFlags.NonPublic)!;
            var previous = defaults.GetValue(null);
            try
            {
                defaults.SetValue(null, new Dictionary<ServiceLifetime, IEnumerable<Type>>
                    { [ServiceLifetime.Singleton] = [], [ServiceLifetime.Scoped] = [] });
                typeof(Program).GetMethod("AddDefaultRules", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [serviceCollection, typeof(Program).Assembly]);
            }
            finally { defaults.SetValue(null, previous); }
            serviceCollection.RemoveAll<IDatabase>();
            serviceCollection.AddSingleton(database);
            serviceCollection.AddSingleton(TimeProvider.System);
            serviceCollection.AddOptions();
            serviceCollection.AddLogging(logging => logging.AddProvider(_logs));
            try
            {
                Services = serviceCollection.BuildServiceProvider();
                ExceptionLogger.Configure(Services.GetRequiredService<ILoggerFactory>());
                Set("_database", database);
                Set("_game", Services.GetRequiredService<IGame>());
                Set("_settingsManager", Services.GetRequiredService<ISettingsManager>());
                Set("_languageManager", Services.GetRequiredService<ILanguageManager>());
                Set("_figureManager", Services.GetRequiredService<IFigureDataManager>());
                Set("_itemDataManager", Services.GetRequiredService<IItemDataManager>());
                Set("_defaultEncoding", Encoding.UTF8);
                PlusEnvironment.CultureInfo = CultureInfo.InvariantCulture;
                DefaultTypeMap.MatchNamesWithUnderscores = true;
                SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static void Set(string field, object value) => typeof(PlusEnvironment)
            .GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

        public void Dispose()
        {
            try { Services?.Dispose(); }
            finally
            {
                foreach (var (field, value) in _globals) field.SetValue(null, value);
                DefaultTypeMap.MatchNamesWithUnderscores = _matchUnderscores;
            }
        }
    }

    private sealed class ErrorLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => new ErrorLogger(Errors);
        public void Dispose() { }
    }

    private sealed class ErrorLogger(ConcurrentQueue<string> errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) errors.Enqueue(formatter(state, error) + " " + error);
        }
    }
}
