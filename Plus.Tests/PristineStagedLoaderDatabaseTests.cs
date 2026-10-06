using System.Runtime.CompilerServices;
using Dapper;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Logs;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("Pristine staged loaders")]
public sealed class PristineStagedLoaderDatabaseTests
{
    [StagedLoaderDatabaseFact]
    public void RoomBansLoadUnsignedUsersAndMigratedUtcExpiry()
    {
        PristineStagedDatabase.Run(["room_bans"], (database, connection) =>
        {
            var store = (IRoomBanStore)new RoomBansComponent(database, TimeProvider.System);
            Assert.Empty(store.Load(42));
            var now = connection.ExecuteScalar<int>("SELECT UNIX_TIMESTAMP()");
            var expires = DateTimeOffset.FromUnixTimeSeconds(now + 3600);
            connection.Execute("ALTER TABLE room_bans MODIFY expire INT NOT NULL DEFAULT 0");
            connection.Execute("INSERT INTO room_bans (user_id, room_id, expire) VALUES (8, 42, @expired), (9, 99, @expires)",
                new
                {
                    expired = now - 30,
                    expires = now + 3600
                });
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/22_UseUtcRoomBanExpiry.sql")));
            Assert.Equal(expires, Assert.Single(store.Load(99)).ExpiresAt);
            store.Save(42, 7, expires);
            Assert.Equal(new RoomBan(7, expires), Assert.Single(store.Load(42)));
            Assert.Equal([7], store.ActiveUserIds(42));
            store.Delete(42, 7);
            Assert.Empty(store.Load(42));
            store.Save(42, int.MaxValue, expires);
            Assert.Equal(int.MaxValue, Assert.Single(store.Load(42)).UserId);
            connection.Execute("UPDATE room_bans SET user_id = @id WHERE room_id = 42 AND user_id = @previous",
                new
                {
                    id = 2147483648u,
                    previous = int.MaxValue
                });
            Assert.Throws<OverflowException>(() => store.Load(42).ToArray());
            Assert.Throws<OverflowException>(() => store.ActiveUserIds(42).ToArray());
        });
    }

    [StagedLoaderDatabaseFact]
    public void OwnOffersLoadNativeEnumStatesAndUnsignedOfferIds()
    {
        PristineStagedDatabase.Run(["catalog_marketplace_offers", "catalog_marketplace_data"], (database, connection) =>
        {
            var market = new MarketplaceManager(database, null!, null!, TimeProvider.System);
            var empty = market.OwnOffers(7);
            Assert.Empty(empty.Offers);
            Assert.Equal(0, empty.AccumulatedAmount);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            connection.Execute("ALTER TABLE catalog_marketplace_offers CHANGE listed_at `timestamp` DOUBLE NOT NULL");
            connection.Execute("""
                INSERT INTO catalog_marketplace_offers
                    (offer_id, item_id, user_id, asking_price, total_price, public_name, sprite_id, item_type, `timestamp`, state, extra_data, furni_id, limited_number, limited_stack) VALUES
                    (10, 1, 7, 100, 101, 'active', 123, '1', @now, '1', '', 1000, 7, 20),
                    (11, 1, 7, 150, 151, 'expired', 124, '2', @expired, '1', '', 1001, 0, 0),
                    (12, 1, 7, 250, 251, 'sold', 125, '1', @expired, '2', '', 1002, 0, 0),
                    (13, 1, 8, 900, 901, 'other owner', 126, '1', @now, '2', '', 1003, 0, 0);
                INSERT INTO catalog_marketplace_data (sprite, avgprice, sold) VALUES (123, 900, 3);
                """, new
            {
                now,
                expired = now - 172860
            });
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/25_UseUtcMarketplaceTimes.sql")));
            Assert.Equal(300, market.AvgPriceForSprite(123));
            var offers = market.OwnOffers(7);
            Assert.Equal(250, offers.AccumulatedAmount);
            Assert.Equal(3, offers.Offers.Count);
            var active = Assert.Single(offers.Offers, offer => offer.OfferId == 10);
            Assert.Equal((1, 123, 7, 20, 101), (active.State, active.SpriteId, active.LimitedNumber, active.LimitedStack, active.TotalPrice));
            Assert.InRange(active.MinutesRemaining, 2879, 2880);
            var expired = Assert.Single(offers.Offers, offer => offer.OfferId == 11);
            Assert.Equal((3, 0), (expired.State, expired.MinutesRemaining));
            var sold = Assert.Single(offers.Offers, offer => offer.OfferId == 12);
            Assert.Equal(2, sold.State);
            Assert.True(sold.MinutesRemaining < 0);
            connection.Execute("UPDATE catalog_marketplace_offers SET offer_id = @id WHERE offer_id = 10", new
            {
                id = int.MaxValue
            });
            Assert.Contains(market.OwnOffers(7).Offers, offer => offer.OfferId == int.MaxValue);
            connection.Execute("UPDATE catalog_marketplace_offers SET offer_id = @id WHERE offer_id = @previous",
                new
                {
                    id = 2147483648u,
                    previous = int.MaxValue
                });
            Assert.Throws<OverflowException>(() => market.OwnOffers(7));
        });
    }

    [StagedLoaderDatabaseFact]
    public void GroupMembersLoadNativeRanksAndRemoveOnlyStaleRequests()
    {
        PristineStagedDatabase.Run(["group_memberships", "group_requests"], (database, connection) =>
        {
            var memberships = new GroupMembershipLoader(database);
            Group Load() => new(42, "group", "", "", 0, 7, null, 0, 1, 1, 0, false, memberships.Load(42));
            Assert.Empty(Load().GetAllMembers);
            connection.Execute("""
                INSERT INTO group_memberships (group_id, user_id, `rank`) VALUES
                    (42, 7, '0'), (42, 7, '0'), (42, 8, '1'), (42, 9, '2'), (99, 10, '0');
                INSERT INTO group_requests (group_id, user_id) VALUES (42, 7), (42, 11), (99, 12);
                """);
            var group = Load();
            Assert.Equal([7], group.GetMembers);
            Assert.Equal([8, 9], group.GetAdministrators.OrderBy(id => id));
            Assert.Equal([11], group.GetRequests);
            Assert.Equal(3, group.MemberCount);
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests WHERE group_id = 42 AND user_id = 7"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests WHERE group_id = 99 AND user_id = 12"));
            connection.Execute("UPDATE group_memberships SET user_id = @id WHERE group_id = 42 AND user_id = 7", new
            {
                id = int.MaxValue
            });
            Assert.Equal([int.MaxValue], Load().GetMembers);
            connection.Execute("UPDATE group_memberships SET user_id = @id WHERE group_id = 42 AND user_id = @previous",
                new
                {
                    id = 2147483648u,
                    previous = int.MaxValue
                });
            Assert.Throws<OverflowException>(() => Load());
            connection.Execute("UPDATE group_memberships SET user_id = @id WHERE group_id = 42 AND user_id = @overflow; " +
                "UPDATE group_requests SET user_id = @overflow WHERE group_id = 42 AND user_id = 11",
                new
                {
                    id = int.MaxValue,
                    overflow = 2147483648u
                });
            Assert.Throws<OverflowException>(() => Load());
        });
    }

    [StagedLoaderDatabaseFact]
    public void RoomChatlogsLoadUnsignedUsersAndKeepMigratedTimesAndIdOrdering()
    {
        PristineStagedDatabase.Run(["chatlogs"], (database, connection) =>
        {
            var data = (RoomData)RuntimeHelpers.GetUninitializedObject(typeof(RoomData));
            data.Id = 42;
            data.Name = "room";
            var room = new Room(data, [], TestLogging.Navigation, TestLogging.Logger, TestRoomAchievements.Unused, TestRoomOwners.Unused);
            var rooms = PristineStagedDatabase.Proxy<IRoomManager>((method, args) =>
            {
                if (method != "TryGetRoom")
                {
                    throw new InvalidOperationException(method);
                }

                args[1] = (uint)args[0]! == room.Id ? room : null;

                return args[1] != null;
            });
            var chatlogs = new Chatlogs();
            var history = new ModeratorHistoryService(database, rooms, new Users(), chatlogs, TimeProvider.System);
            Assert.Empty(Assert.IsType<ModeratorRoomChatlog>(history.GetRoomChatlog(42)).Entries);
            Assert.Null(history.GetRoomChatlog(99));
            connection.Execute("ALTER TABLE chatlogs MODIFY `timestamp` DOUBLE NOT NULL");
            connection.Execute("""
                INSERT INTO chatlogs (id, user_id, room_id, message, `timestamp`) VALUES
                    (1, 7, 42, 'first id', 1700000030), (2, 8, 42, '', 1700000000.25),
                    (3, 9, 42, 'missing user', 1700000040), (4, 7, 99, 'other room', 1700000050);
                """);
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/24_UseUtcChatlogTimes.sql")));
            var result = Assert.IsType<ModeratorRoomChatlog>(history.GetRoomChatlog(42));
            Assert.Equal(new ModeratorRoomIdentity(42, "room"), result.Room);
            Assert.Collection(result.Entries,
                entry => Assert.Equal(new ModeratorChatEntry(8, "Bob", "", DateTimeOffset.FromUnixTimeMilliseconds(1700000000250)), entry),
                entry => Assert.Equal(new ModeratorChatEntry(7, "Alice", "first id", DateTimeOffset.FromUnixTimeSeconds(1700000030)), entry));
            Assert.Equal(2, chatlogs.Flushes);
            connection.Execute("INSERT INTO chatlogs (id, user_id, room_id, message, `timestamp`) VALUES (5, @id, 42, 'boundary', @createdAt)",
                new
                {
                    id = int.MaxValue,
                    createdAt = DateTimeOffset.FromUnixTimeSeconds(1700000060).UtcDateTime
                });
            Assert.Equal(int.MaxValue, history.GetRoomChatlog(42)!.Entries[0].UserId);
            connection.Execute("UPDATE chatlogs SET user_id = @id WHERE id = 5", new
            {
                id = 2147483648u
            });
            Assert.Throws<OverflowException>(() => history.GetRoomChatlog(42));
        });
    }

    private sealed class Users : IModeratorUserLookup
    {
        public Habbo? GetById(int userId) => userId switch
        {
            7 => new() { Id = 7, Username = "Alice" },
            8 => new() { Id = 8, Username = "Bob" },
            int.MaxValue => new() { Id = int.MaxValue, Username = "Boundary" },
            _ => null
        };
    }

    private sealed class Chatlogs : IChatlogManager
    {
        public int Flushes
        {
            get; private set;
        }
        public void FlushAndSave() => Flushes++;
        public void StoreChatlog(ChatlogEntry entry) => throw new NotSupportedException();
    }
}
