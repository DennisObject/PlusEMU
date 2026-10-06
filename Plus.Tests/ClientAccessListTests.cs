using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Subscriptions;
using Xunit;

namespace Plus.Tests;

public sealed class ClientAccessListTests
{
    internal static ChatStyleManager Styles()
    {
        var manager = new ChatStyleManager(NullLogger<ChatStyleManager>.Instance, null!);
        var styles = (Dictionary<int, ChatStyle>)typeof(ChatStyleManager).GetField("_styles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;

        foreach (var style in new[] {
            new ChatStyle(0, "Normal", ""),
            new ChatStyle(1, "HC", "", true),
            new ChatStyle(2, "Staff", PermissionKeys.ChatStyleStaff),
            new ChatStyle(3, "Disabled", "", enabled: false),
            new ChatStyle(4, "Ambassador", PermissionKeys.Ambassador)
        })
        {
            styles.Add(style.Id, style);
        }

        return manager;
    }

    internal static RoomManager Models()
    {
        var manager = new RoomManager(NullLogger<RoomManager>.Instance, null!, null!, TimeProvider.System, new TestRoomFactory(), new TestRoomDataLoaderFactory());
        var models = (Dictionary<string, RoomModel>)typeof(RoomManager).GetField("_roomModels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;

        foreach (var model in new[] {
            new RoomModel("model_a", 0, 0, 0, 0, "00\rx0", 0, 0, false),
            new RoomModel("model_s", 0, 0, 0, 0, "00\r00", 0, 0, false),
            new RoomModel("model_wl", 0, 0, 0, 0, "00\r00", 0, 0, false),
            new RoomModel("model_hc", 0, 0, 0, 0, "00\r00", 1, 0, false),
            new RoomModel("model_vip", 0, 0, 0, 0, "00\r00", 2, 0, false),
            new RoomModel("model_staff", 0, 0, 0, 0, "00\r00", -1, 0, false),
            new RoomModel("model_gated", 0, 0, 0, 0, "00\r00", 0, 0, false) { RequiredPermission = PermissionKeys.ChatStyleStaff },
            new RoomModel("model_custom", 0, 0, 0, 0, "00\r00", 0, 0, true)
        })
        {
            models.Add(model.Id, model);
        }

        return manager;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ListsResolveMembershipAndComplimentaryClubWithoutClientConfig(bool purchased, bool complimentary)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var access = UserAccess.Create([], complimentary ? [new(PermissionKeys.ClubAccess, false)] : [],
            membership: purchased ? new ClubMembership(DateTimeOffset.FromUnixTimeSeconds(now + 60), DateTimeOffset.FromUnixTimeSeconds(now), DateTimeOffset.FromUnixTimeSeconds(now)) : ClubMembership.None);
        Assert.Equal(purchased || complimentary ? new[] { 0, 1 } : new[] { 0 }, Styles().GetAllowedStyleIds(access));
        Assert.Equal(purchased || complimentary ? new[] { "model_a", "model_hc", "model_vip" } : new[] { "model_a" }, Models().GetCreatableModels(access).Select(model => model.Id));
    }

    [Fact]
    public void ListsHonorPermissionsDeniesAndDisabledStylesAndExcludeCustomModels()
    {
        var access = UserAccess.Create([], [new("*", false)]);
        Assert.Equal(new[] { 0, 1, 2, 4 }, Styles().GetAllowedStyleIds(access));
        Assert.Equal(new[] { "model_a", "model_gated", "model_hc", "model_staff", "model_vip" }, Models().GetCreatableModels(access).Select(model => model.Id));
        var denied = UserAccess.Create([], [new("*", false), new(PermissionKeys.ChatStyleStaff, true), new(PermissionKeys.ClubAccess, true)]);
        Assert.Equal(new[] { 0, 4 }, Styles().GetAllowedStyleIds(denied));
        Assert.Equal(new[] { "model_a", "model_staff" }, Models().GetCreatableModels(denied).Select(model => model.Id));
    }

    [Fact]
    public void ResolversDropClubAndPermissionAtExactExpiry()
    {
        var clock = new ClubMembershipTests.Clock();
        var now = clock.Now.ToUnixTimeSeconds();
        var access = UserAccess.Create([], [new(PermissionKeys.ChatStyleStaff, false, clock.Now.AddSeconds(60))],
            clock: clock, membership: new(clock.Now.AddSeconds(60), clock.Now, clock.Now));
        Assert.Equal(new[] { 0, 1, 2 }, Styles().GetAllowedStyleIds(access));
        Assert.Equal(4, Models().GetCreatableModels(access).Count);
        clock.Now = clock.Now.AddSeconds(60);
        Assert.Equal(new[] { 0 }, Styles().GetAllowedStyleIds(access));
        Assert.Equal(new[] { "model_a" }, Models().GetCreatableModels(access).Select(model => model.Id));
    }

    [Fact]
    public void LegacyModelsRemainLoadableButCannotBeOfferedOrCreated()
    {
        var manager = Models();
        var staff = UserAccess.Create([], [new("*", false)]);

        foreach (var id in new[] { "model_s", "model_wl" })
        {
            Assert.True(manager.TryGetModel(id, out var model));
            Assert.False(model.CanCreate(UserAccess.Empty));
            Assert.False(model.CanCreate(staff));
        }

        Assert.DoesNotContain(manager.GetCreatableModels(staff), model => model.Id is "model_s" or "model_wl");
    }

    [Fact]
    public void ComposersCarryIdsAndServerDerivedModelGeometry()
    {
        var styles = new HabbiconTestSupport.RecordingPacket();
        new AllowedChatStylesComposer(Styles().GetAllowedStyleIds(UserAccess.Empty)).Compose(styles);
        Assert.Equal(new object[] { 1, 0 }, styles.Writes);
        Assert.Equal(3, new RoomModel("ragged", 0, 0, 0, 0, "00\r0", 0, 0, false).TileSize);
        var models = new HabbiconTestSupport.RecordingPacket();
        new CreatableRoomModelsComposer(CreatableRoomModelSnapshot.Capture(Models().GetCreatableModels(UserAccess.Empty))).Compose(models);
        Assert.Equal(new object[] { 1, "model_a", 3, 2, 2, 0 }, models.Writes);
    }
}
