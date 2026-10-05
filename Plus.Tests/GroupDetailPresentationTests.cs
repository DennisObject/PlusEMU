using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.Communication.Packets.Outgoing;
using Plus.Core.Settings;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class GroupDetailPresentationTests
{
    [Fact]
    public async Task HandlersOnlyDecodeAndDelegate()
    {
        var calls = new List<(string Method, object?[] Arguments)>();
        var presentation = CatalogSnapshotTestSupport.Proxy<IGroupPresentationService>((method, args) =>
        {
            calls.Add((method, args));
            return null;
        });
        await new GetGroupInfoEvent(presentation).Parse(null!, HabbiconTestSupport.Incoming(9, true));
        await new GetGroupFurniSettingsEvent(presentation).Parse(null!, HabbiconTestSupport.Incoming(42, 9));
        Assert.Equal(nameof(IGroupPresentationService.ShowInfo), calls[0].Method);
        Assert.Equal(new object?[] { null, 9, true }, calls[0].Arguments);
        Assert.Equal(nameof(IGroupPresentationService.ShowFurnitureSettings), calls[1].Method);
        Assert.Equal(new object?[] { null, 42u, 9 }, calls[1].Arguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupInfoPreservesNewWindowFlagAndMissingGroupSilence(bool newWindow)
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var service = Service(Group());
        service.ShowInfo(client, 9, newWindow);
        Assert.Equal(ServerPacketHeader.GroupInfoComposer, Assert.Single(sent).Header);
        Assert.Equal(newWindow ? 1 : 0, sent[0].Payload[^7]);
        Service(null).ShowInfo(client, 100, newWindow);
        Assert.Single(sent);
    }

    [Theory]
    [InlineData(InteractionType.GuildGate, false, true)]
    [InlineData(InteractionType.GuildGate, true, false)]
    [InlineData(InteractionType.GuildItem, false, false)]
    public void FurnitureSettingsValidateTheItemAndPreservePacketOrder(InteractionType type, bool temporary, bool allowed)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        var items = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, items);
        var item = new Item
        {
            Id = 92, RoomId = 42, IsTemporary = temporary,
            Definition = new ItemDefinition { Id = 92, Type = ItemType.Floor, InteractionType = type },
            ExtraData = FurniObjectData.Empty
        };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling)
            .GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(items)!;
        floor[item.Id] = item;
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7, CurrentRoom = room });
        Service(Group()).ShowFurnitureSettings(client, item.Id, 9);
        if (allowed)
            Assert.Equal(new[] { ServerPacketHeader.GroupFurniSettingsComposer, ServerPacketHeader.GroupInfoComposer }, sent.Select(p => p.Header));
        else
            Assert.Empty(sent);
        sent.Clear();
        Service(Group()).ShowFurnitureSettings(client, 999, 9);
        Assert.Empty(sent);
        Service(null).ShowFurnitureSettings(client, item.Id, 99);
        Assert.Empty(sent);
    }

    [Fact]
    public void FurnitureRequestWithoutCurrentRoomPublishesNothing()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        Service(Group()).ShowFurnitureSettings(client, 92, 9);
        Assert.Empty(sent);
    }

    private static Group Group() => new(9, "Crew", "", "b01014s02024", 42, 7, null,
        0, 1, 1, 0, false, GroupMembershipSnapshot.Empty);

    private static GroupPresentationService Service(Group? group) => new(
        CatalogSnapshotTestSupport.Proxy<IGroupManager>((_, args) => { args[1] = group; return group != null; }),
        CatalogSnapshotTestSupport.Proxy<ICacheManager>((_, _) => throw new NotSupportedException()),
        CatalogSnapshotTestSupport.Proxy<IRoomDataLoader>((_, _) => throw new NotSupportedException()),
        CatalogSnapshotTestSupport.Proxy<ISettingsManager>((_, _) => throw new NotSupportedException()),
        CatalogSnapshotTestSupport.Proxy<IGroupInfoSnapshotService>((_, _) => new GroupInfoSnapshot(
            group!.Id, group.Type, group.Name, group.Description, group.Badge, group.RoomId, "HQ", 0,
            "1-1-1970", "Owner", true, false, false, false, 0, true, false)));
}
