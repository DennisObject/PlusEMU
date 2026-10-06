using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public sealed class RoomDataLoaderTests
{
    [Fact]
    public void LoadedRoomReturnsItsExistingDataWithoutDatabaseOrMetadataReads()
    {
        var expected = new RoomData { Id = 42, Name = "loaded" };
        var room = new Room(expected, [], NullLogger<RoomNavigation>.Instance, NullLogger.Instance, TestRoomAchievements.Unused, TestRoomOwners.Unused);
        var manager = Proxy<IRoomManager>((method, args) =>
        {
            Assert.Equal("TryGetRoom", method);
            args[1] = room;

            return true;
        });
        var loader = new RoomDataLoaderFactory(EditorTestSupport.UntouchableDatabase(),
            Proxy<IGroupManager>((method, _) => throw new InvalidOperationException(method)),
            Proxy<IRoomPromotionLoader>((method, _) => throw new InvalidOperationException(method))).Create(manager);

        Assert.True(loader.TryGetData(42, out var actual));
        Assert.Same(expected, actual);
    }

    [Fact]
    public void ManagerUsesItsRequiredLoaderBeforeConstructingAnUnloadedRoom()
    {
        var expected = new RoomData { Id = 42, Name = "canonical" };
        var reads = 0;
        var loader = Proxy<IRoomDataLoader>((method, args) =>
        {
            Assert.Equal("TryGetData", method);
            reads++;
            args[1] = (uint)args[0]! == 42 ? expected : null;

            return (uint)args[0]! == 42;
        });
        IRoomManager? boundManager = null;
        var dataFactory = Proxy<IRoomDataLoaderFactory>((method, args) =>
        {
            Assert.Equal("Create", method);
            boundManager = (IRoomManager)args[0]!;

            return loader;
        });
        var construction = new InvalidOperationException("room construction reached");
        var roomFactory = Proxy<IRoomFactory>((method, args) =>
        {
            Assert.Equal("Create", method);
            Assert.Same(expected, args[0]);
            throw construction;
        });
        var manager = new RoomManager(NullLogger<RoomManager>.Instance,
            EditorTestSupport.UntouchableDatabase(), null!, TimeProvider.System, roomFactory, dataFactory);

        Assert.Same(manager, boundManager);
        Assert.False(manager.TryLoadRoom(404, out var missing));
        Assert.Null(missing);
        Assert.Same(construction, Assert.Throws<InvalidOperationException>(() => manager.TryLoadRoom(42, out _)));
        Assert.Equal(2, reads);
    }

    [RoomComponentDatabaseFact]
    public void MariaRowsMaterializeWithModelsMetadataFallbacksAndOwnerOrdering()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "room_data_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");

        try
        {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE users (id INT PRIMARY KEY, username VARCHAR(100) NULL);
                CREATE TABLE rooms (
                    id INT UNSIGNED PRIMARY KEY, owner INT NOT NULL, caption VARCHAR(100) NOT NULL,
                    model_name VARCHAR(50) NOT NULL, password VARCHAR(50) NOT NULL DEFAULT '', score INT NOT NULL DEFAULT 0,
                    roomtype VARCHAR(20) NOT NULL DEFAULT 'private', state VARCHAR(20) NOT NULL DEFAULT 'open',
                    users_now INT NOT NULL DEFAULT 0, users_max INT NOT NULL DEFAULT 25, category INT NOT NULL DEFAULT 0,
                    description VARCHAR(255) NOT NULL DEFAULT '', tags VARCHAR(255) NOT NULL DEFAULT '',
                    floor VARCHAR(20) NOT NULL DEFAULT '0.0', landscape VARCHAR(20) NOT NULL DEFAULT '0.0',
                    allow_pets BOOL NOT NULL DEFAULT FALSE, allow_pets_eat BOOL NOT NULL DEFAULT FALSE,
                    room_blocking_disabled BOOL NOT NULL DEFAULT FALSE, allow_hidewall BOOL NOT NULL DEFAULT FALSE,
                    wallthick INT NOT NULL DEFAULT 0, floorthick INT NOT NULL DEFAULT 0, wallpaper VARCHAR(20) NOT NULL DEFAULT '0.0',
                    mute_settings INT NOT NULL DEFAULT 0, ban_settings INT NOT NULL DEFAULT 0, kick_settings INT NOT NULL DEFAULT 0,
                    chat_mode INT NOT NULL DEFAULT 0, chat_size INT NOT NULL DEFAULT 0, chat_speed INT NOT NULL DEFAULT 0,
                    chat_extra_flood INT NOT NULL DEFAULT 0, chat_hearing_distance INT NOT NULL DEFAULT 0,
                    trade_settings INT NOT NULL DEFAULT 0, group_id INT NOT NULL DEFAULT 0, sale_price INT NOT NULL DEFAULT 0,
                    push_enabled BOOL NOT NULL DEFAULT FALSE, pull_enabled BOOL NOT NULL DEFAULT FALSE,
                    spush_enabled BOOL NOT NULL DEFAULT FALSE, spull_enabled BOOL NOT NULL DEFAULT FALSE,
                    enables_enabled BOOL NOT NULL DEFAULT FALSE, respect_notifications_enabled BOOL NOT NULL DEFAULT FALSE,
                    pet_morphs_allowed BOOL NOT NULL DEFAULT FALSE, lay_enabled BOOL NOT NULL DEFAULT FALSE);
                INSERT INTO users VALUES (7, 'owner'), (8, '');
                INSERT INTO rooms (id, owner, caption, model_name) VALUES
                    (1, 7, 'Zulu', 'model_a'), (3, 7, 'Missing model', 'unknown');
                INSERT INTO rooms (id, owner, caption, model_name, state, tags, allow_pets, allow_pets_eat,
                    room_blocking_disabled, allow_hidewall, kick_settings, group_id, push_enabled, pull_enabled,
                    spush_enabled, spull_enabled, enables_enabled, respect_notifications_enabled, pet_morphs_allowed, lay_enabled)
                    VALUES (2, 7, 'Alpha', 'model_a', 'password', 'one,two', TRUE, TRUE, TRUE, TRUE, 2, 9,
                        TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE);
                INSERT INTO rooms (id, owner, caption, model_name) VALUES (4, 999, 'Orphan', 'model_a');
                INSERT INTO rooms (id, owner, caption, model_name) VALUES (5, 8, 'Empty owner name', 'model_a');
                """);

            var model = new RoomModel("model_a", 0, 0, 0, 0, "0", 0, 0, false);
            var manager = Proxy<IRoomManager>((method, args) => method switch
            {
                "TryGetRoom" => Missing(args),
                "TryGetModel" => Model(args, model),
                _ => throw new InvalidOperationException(method)
            });
            var group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
            group.Id = 9;
            group.Name = "group";
            var groups = Proxy<IGroupManager>((method, args) =>
            {
                Assert.Equal("TryGetGroup", method);
                args[1] = (int)args[0]! == 9 ? group : null;

                return (int)args[0]! == 9;
            });
            var promotions = new RecordingPromotionLoader();
            var loader = new RoomDataLoader(new ProbeDatabase(new MySqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
            {
                Database = schema
            }.ConnectionString),
                manager, groups, promotions);

            Assert.False(loader.TryGetData(404, out _));
            Assert.False(loader.TryGetData(3, out _));
            Assert.False(loader.TryGetData(4, out _));
            Assert.True(loader.TryGetData(5, out var unnamedOwner));
            Assert.Equal("Habboon", unnamedOwner.OwnerName);

            Assert.True(loader.TryGetData(2, out var materialized));
            Assert.Equal((RoomAccess.Password, 2, "one", "two"),
                (materialized.Access, materialized.WhoCanKick, materialized.Tags[0], materialized.Tags[1]));
            Assert.True(materialized.AllowPets);
            Assert.True(materialized.AllowPetsEating);
            Assert.True(materialized.RoomBlockingEnabled);
            Assert.True(materialized.Hidewall);
            Assert.True(materialized.PushEnabled && materialized.PullEnabled && materialized.SuperPushEnabled && materialized.SuperPullEnabled);
            Assert.True(materialized.EnablesEnabled && materialized.RespectNotificationsEnabled && materialized.PetMorphsAllowed && materialized.LayEnabled);
            Assert.Same(group, materialized.Group);
            Assert.Same(promotions.Promotion, materialized.Promotion);

            promotions.Loaded.Clear();
            var owned = loader.GetRoomsDataByOwnerSortByName(7);
            Assert.Equal(["Alpha", "Zulu"], owned.Select(roomData => roomData.Name).ToArray());
            Assert.Equal([2u, 1u], promotions.Loaded);
        }
        finally
        {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static bool Missing(object?[] args)
    {
        args[1] = null;

        return false;
    }

    private static bool Model(object?[] args, RoomModel model)
    {
        var found = (string)args[0]! == model.Id;
        args[1] = found ? model : null;

        return found;
    }

    private static T Proxy<T>(Func<string, object?[], object?> callback) where T : class
    {
        var proxy = DispatchProxy.Create<T, CallbackProxy>();
        ((CallbackProxy)(object)proxy).Callback = callback;

        return proxy;
    }

    private class CallbackProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Callback { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Callback(targetMethod!.Name, args!);
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }

    private sealed class RecordingPromotionLoader : IRoomPromotionLoader
    {
        public RoomPromotion Promotion
        {
            get;
        } = new("promotion", "details",
            new DateTimeOffset(2039, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2039, 1, 1, 1, 0, 0, TimeSpan.Zero), 1, TimeProvider.System);
        public List<uint> Loaded { get; } = [];

        public RoomPromotion? Load(uint roomId)
        {
            Loaded.Add(roomId);

            return Promotion;
        }
    }
}
