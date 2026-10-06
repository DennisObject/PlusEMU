using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Core.Settings;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class GroupCreationPresentationTests
{
    [Fact]
    public async Task HandlerDelegatesWithoutReadingAnIncomingField()
    {
        var service = new RecordingPresentation();
        await new GetGroupCreationWindowEvent(service).Parse(null!, null!);
        Assert.True(service.Shown);
    }

    [Fact]
    public void ServiceUsesOwnerLoaderAndCapturesOnlyRoomsWithoutGroups()
    {
        var source = new List<RoomData>
        {
            new() { Id = 2, Name = "Alpha" },
            new() { Id = 3, Name = "Grouped", Group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group)) },
            new() { Id = 4, Name = "Beta" }
        };
        var loader = CatalogSnapshotTestSupport.Proxy<IRoomDataLoader>((method, args) =>
        {
            Assert.Equal(nameof(IRoomDataLoader.GetRoomsDataByOwnerSortByName), method);
            Assert.Equal(7, args[0]);

            return source;
        });
        var settings = CatalogSnapshotTestSupport.Proxy<ISettingsManager>((_, args) =>
        {
            Assert.Equal("catalog.group.purchase.cost", args[0]);

            return "20";
        });
        var service = new GroupPresentationService(
            CatalogSnapshotTestSupport.Proxy<IGroupManager>((_, _) => throw new NotSupportedException()),
            CatalogSnapshotTestSupport.Proxy<ICacheManager>((_, _) => throw new NotSupportedException()), loader, settings,
            CatalogSnapshotTestSupport.Proxy<IGroupInfoSnapshotService>((_, _) => throw new NotSupportedException()));
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });

        service.ShowCreationWindow(client);

        var payload = Assert.Single(sent).Payload;
        Assert.Equal(20, BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0, 4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4, 4)));
    }

    [Fact]
    public void ScalarSnapshotPreservesAllFieldsAfterRoomAndListMutation()
    {
        var room = new RoomData { Id = 2, Name = "Alpha" };
        var source = new List<RoomData> { room };
        var presentation = new GroupCreationPresentation(20,
            source.Select(value => new GroupCreationRoom(value.Id, value.Name)).ToImmutableArray());
        var composer = new GroupCreationWindowComposer(presentation);
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);

        room.Id = 99;
        room.Name = "Changed";
        source.Clear();
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);

        Assert.Equal(new object[] { 20, 1, 2u, "Alpha", false, 5, 5, 11, 4, 6, 11, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, before.Writes);
        Assert.Equal(before.Writes, after.Writes);
    }

    private sealed class RecordingPresentation : IGroupPresentationService
    {
        public bool Shown
        {
            get; private set;
        }
        public void ShowCreationWindow(GameClient session) => Shown = true;
        public void ShowMembers(GameClient session, GroupMembersRequest request) => throw new NotSupportedException();
        public void ShowBadgeEditor(GameClient session) => throw new NotSupportedException();
        public void ShowInfo(GameClient session, int groupId, bool newWindow) => throw new NotSupportedException();
        public void ShowFurnitureSettings(GameClient session, uint itemId, int groupId) => throw new NotSupportedException();
        public void ShowCatalogFurnitureConfiguration(GameClient session) => throw new NotSupportedException();
    }
}
