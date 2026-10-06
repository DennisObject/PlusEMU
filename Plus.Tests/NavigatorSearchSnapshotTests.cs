using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Plus.HabboHotel.Groups;
using Plus.Communication.Packets.Outgoing.Navigator.New;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class NavigatorSearchSnapshotTests
{
    [Theory]
    [InlineData(NavigatorCategoryType.MyRooms, "")]
    [InlineData(NavigatorCategoryType.MyFavourites, "")]
    [InlineData(NavigatorCategoryType.MyGroups, "")]
    [InlineData(NavigatorCategoryType.MyRights, "")]
    [InlineData(NavigatorCategoryType.Query, "owner:Alice")]
    [InlineData(NavigatorCategoryType.Query, "HQ")]
    public void ResolvingCategoriesUseTheRequiredRoomLoader(NavigatorCategoryType category, string query)
    {
        var ids = new uint[] { 2, 99, 1, 2 };
        var result = new SearchResultList(7, "cat", "id", "Name", true, 0, "", NavigatorViewMode.Thumbnail, "query", "none", 0)
        {
            CategoryType = category
        };
        var navigator = CatalogSnapshotTestSupport.Proxy<INavigatorManager>((method, args) => method switch
        {
            nameof(INavigatorManager.GetCategoriessForSearch) => new List<SearchResultList> { result },
            nameof(INavigatorManager.TryGetSearchResultList) => FoundResult(args, result),
            nameof(INavigatorManager.TryGetFeaturedRoom) => NoFeatured(args),
            _ => throw new InvalidOperationException(method)
        });
        var storeCalls = new List<string>();
        var store = CatalogSnapshotTestSupport.Proxy<INavigatorSearchStore>((method, _) =>
        {
            storeCalls.Add(method);

            return method == nameof(INavigatorSearchStore.FindByCaption)
                ? new NavigatorRoomReference[] { new() { Id = 3, Visible = false }, new() { Id = 2, Visible = true },
                    new() { Id = 99, Visible = true }, new() { Id = 1, Visible = true }, new() { Id = 2, Visible = true } }
                : (object)ids;
        });
        var groups = CatalogSnapshotTestSupport.Proxy<IGroupManager>((method, args) =>
        {
            Assert.Equal(nameof(IGroupManager.GetGroupsForUser), method);
            Assert.Equal(42, args[0]);

            return ids.Select((id, index) => new Group(index, "Group", "", "", id, 42, null, 0, 1, 1, 0, false, GroupMembershipSnapshot.Empty)).ToList();
        });
        var loader = new Rooms();
        var (client, _) = HabbiconTestSupport.Client(new()
        {
            Id = 42
        });

        foreach (var id in ids)
        {
            client.GetHabbo().FavoriteRooms.Add(id);
        }

        var roomManager = CatalogSnapshotTestSupport.Proxy<IRoomManager>((method, _) => throw new InvalidOperationException(method));
        var service = new NavigatorSearchService(navigator, store, roomManager, groups, loader);

        var snapshot = service.Search(client, "cat", query);

        Assert.Equal(new uint[] { 2, 1 }, Assert.Single(snapshot.Results).Rooms.Select(room => room.Id));

        if (category == NavigatorCategoryType.MyRooms)
        {
            Assert.Equal(42, loader.OwnerId);
            Assert.Empty(loader.Resolved);
        }
        else
        {
            Assert.Equal(ids, loader.Resolved);
        }

        Assert.Equal(category == NavigatorCategoryType.MyRights ? [nameof(INavigatorSearchStore.FindWithRights)] :
            query.StartsWith("owner:") ? [nameof(INavigatorSearchStore.FindByOwnerName)] :
            query.Length > 0 ? [nameof(INavigatorSearchStore.FindByCaption)] : Array.Empty<string>(), storeCalls);
    }

    private static bool FoundResult(object?[] args, SearchResultList result)
    {
        args[1] = result;

        return true;
    }
    private static bool NoFeatured(object?[] args)
    {
        args[1] = null;

        return false;
    }

    private sealed class Rooms : IRoomDataLoader
    {
        public int? OwnerId
        {
            get; private set;
        }
        public List<uint> Resolved { get; } = [];
        public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId)
        {
            OwnerId = ownerId;

            return [new() { Id = 1, Name = "A", UsersNow = 1 }, new() { Id = 2, Name = "B", UsersNow = 2 }];
        }
        public bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data)
        {
            Resolved.Add(roomId);
            data = roomId == 99 ? null : new RoomData { Id = roomId, Name = "Room" };

            return data != null;
        }
    }

    [Fact]
    public void ComposerWritesExactCategoryAndRoomBlockFields()
    {
        var room = new RoomWireData(42, "HQ", 7, "Alice", 1, 3, 25, "Desc", 2, 9, 4, ["one", "two"], true, true, "featured.png",
            new(8, "Group", "badge"), new("Promo", "Details", 12));
        var snapshot = new NavigatorSearchSnapshot("hotel_view", "query", [new("popular", "Popular", 1, 0, [room])]);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new NavigatorSearchResultSetComposer(snapshot).Compose(packet);

        Assert.Equal(new object[] { "hotel_view", "query", 1, "popular", "Popular", 1, false, 0, 1, (uint)42, "HQ", 7, "Alice", 1, 3, 25,
            "Desc", 2, 9, 0, 4, 2, "one", "two", 31, "featured.png", 8, "Group", "badge", "Promo", "Details", 12 }, packet.Writes);
    }

    [Fact]
    public void ComposerReusesFrozenSnapshotWithoutSessionRoomOrQueryAccess()
    {
        var mutableTags = new[] { "original" };
        var room = new RoomWireData(1, "Room", 2, "Owner", 0, 1, 10, "", 0, 0, 0, mutableTags.ToImmutableArray(), false, false, null, null, null);
        var composer = new NavigatorSearchResultSetComposer(new("cat", "", [new("id", "name", 0, 1, [room])]));
        var first = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(first);

        mutableTags[0] = "changed";
        var second = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(second);

        Assert.Equal(first.Writes, second.Writes);
        Assert.Contains("original", second.Writes);
        Assert.DoesNotContain("changed", second.Writes);
        var field = Assert.Single(typeof(NavigatorSearchResultSetComposer).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        Assert.Equal(typeof(NavigatorSearchSnapshot), field.FieldType);
    }
}
