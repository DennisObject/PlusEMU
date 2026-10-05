using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Xunit;

namespace Plus.Tests;

[Collection("Modern Wired database seam")]
public sealed class WiredLegacyAccessDependencyTests
{
    [Fact]
    public void InjectedAccessControlsBadgeStubWithoutGrantingBadge()
    {
        var allowed = new HashSet<int> { 42 };
        var checks = new List<(int UserId, string Key)>();
        var access = TestWiredAccess.Create((method, arguments) =>
        {
            Assert.Equal(nameof(IAccessControl.Can), method.Name);
            var userId = (int)arguments![0]!;
            var key = (string)arguments[1]!;
            checks.Add((userId, key));
            return allowed.Contains(userId);
        });
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.OwnerId = 42;
        var users = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress());
        Set(room, "_roomUserManager", users);
        var wired = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty,
            TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance,
            TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty,
            TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, access);
        Set(room, "_wiredComponent", wired);
        var habbo = new Habbo
        {
            Id = 7,
            Username = "recipient",
            CurrentRoom = room,
            Inventory = new InventoryComponent { Badges = new BadgesInventoryComponent([]) }
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        var user = new RoomUser(habbo.Id, 0, 5, room, client);
        ((ConcurrentDictionary<int, RoomUser>)Get(users, "_users")).TryAdd(user.VirtualId, user);
        var item = Item(42);
        var box = Assert.IsType<GiveUserBadgeBox>(wired.GenerateNewBox(item));
        box.StringData = "TEST_BADGE";

        Assert.True(box.Execute(habbo));
        Assert.Equal([(42, PermissionKeys.RoomItemWiredRewards)], checks);
        Assert.False(habbo.Inventory.Badges.HasBadge("TEST_BADGE"));
        var success = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.BroadcastMessageAlertComposer, success.Header);
        var successBody = new FlashIncomingPacket { Buffer = success.Payload };
        Assert.Equal("You have recieved a badge!", successBody.ReadString());
        Assert.Equal("", successBody.ReadString());
        Assert.False(successBody.HasDataRemaining());

        sent.Clear();
        habbo.Inventory.Badges.AddBadge(new Badge("TEST_BADGE", 0));
        Assert.True(box.Execute(habbo));
        var duplicate = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.WhisperComposer, duplicate.Header);
        var duplicateBody = new FlashIncomingPacket { Buffer = duplicate.Payload };
        Assert.Equal(5, duplicateBody.ReadInt());
        Assert.Equal("Oops, it appears you have already recieved this badge!", duplicateBody.ReadString());
        Assert.Equal(0, duplicateBody.ReadInt());
        Assert.Equal(0, duplicateBody.ReadInt());
        Assert.Equal(0, duplicateBody.ReadInt());
        Assert.Equal(54, duplicateBody.ReadInt());
        Assert.False(duplicateBody.HasDataRemaining());
        Assert.Single(habbo.Inventory.Badges.Badges);

        sent.Clear();
        allowed.Clear();
        Assert.False(box.Execute(habbo));
        Assert.Empty(sent);
        item.UserId = 99;
        Assert.False(box.Execute(habbo));
        Assert.Empty(sent);
        Assert.Equal(new[] { 42, 42, 42, 99 }, checks.Select(check => check.UserId));
    }

    private static Item Item(uint ownerId) => new()
    {
        Id = 77,
        UserId = checked((int)ownerId),
        OwnerId = ownerId,
        ExtraData = new LegacyDataFormat { Data = "0" },
        Definition = new ItemDefinition
        {
            ItemName = "wf_act_give_user_badge",
            WiredType = WiredBoxType.EffectGiveUserBadge,
            InteractionType = InteractionType.WiredEffect
        }
    };

    private static void Set(object value, string field, object data) =>
        value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
    private static object Get(object value, string field) =>
        value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
