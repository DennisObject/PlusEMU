using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Communication.Packets.Outgoing.Inventory.Achievements;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Sound;
using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public sealed class PacketCollectionSnapshotTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData(-1L, 0)]
    [InlineData(2000000000L, 2000000000)]
    [InlineData(2200000000L, int.MaxValue)]
    public void AccessExpirySnapshotsPreserveUtcInstantsAndBoundLegacyWireIntegers(long? seconds, int expected)
    {
        var expiry = seconds.HasValue ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : (DateTimeOffset?)null;
        var member = new AccessMember { Id = 7, Username = "Alice", ExpiresAt = expiry };
        var rule = new AccessOverride { Key = "camera.*", Effect = "deny", Reason = "reason", ExpiresAt = expiry };
        var members = new List<AccessMember> { member };
        var rules = new List<AccessOverride> { rule };
        var memberPacket = new HousekeepingRoleMembersComposer(2, new(3, 4, 5, members));
        var rulePacket = new HousekeepingUserOverridesComposer(2, new(7, "Alice", rules));
        object[] expectedMembers = [2, 3, 4, 5, 1, 7, "Alice", expected];
        object[] expectedRules = [2, 7, "Alice", 1, "camera.*", "deny", "reason", expected];

        Assert.Equal(expectedMembers, Writes(memberPacket));
        Assert.Equal(expectedRules, Writes(rulePacket));
        member.Username = "changed";
        member.ExpiresAt = DateTimeOffset.MaxValue;
        rule.Key = "changed";
        rule.ExpiresAt = DateTimeOffset.MaxValue;
        members.Clear();
        rules.Clear();
        Assert.Equal(expectedMembers, Writes(memberPacket));
        Assert.Equal(expectedRules, Writes(rulePacket));
    }

    [Fact]
    public void FriendRequestsRetainTheirCountOrderAndCapturedIdentity()
    {
        var requests = new List<FriendRequestData> { new(7, "Alice", "look") };
        var composer = new FriendRequestsComposer(requests);
        object[] expected = [1, 1, 7, "Alice", "look"];

        Assert.Equal(expected, Writes(composer));
        requests[0] = new(8, "Bob", "changed");
        requests.Add(new(9, "Third", "other"));
        Assert.Equal(expected, Writes(composer));
        Assert.Equal(expected, Writes(composer));
    }

    [Fact]
    public void SearchResultsRetainOnlineAndOfflineWireFieldsAfterSourceMutation()
    {
        var friend = new SearchResult(7, "Alice", "motto", "look", DateTimeOffset.FromUnixTimeSeconds(2200000000));
        var friends = new List<HabboSearchEntry> { new(friend, true) };
        var others = new List<HabboSearchEntry> { new(new(8, "Bob", "away", "hidden", null), false) };
        var composer = new HabboSearchResultComposer(friends, others);
        object[] expected = [1, 7, "Alice", "motto", true, false, "", 0, "look", "2200000000",
            1, 8, "Bob", "away", false, false, "", 0, "", "0"];

        Assert.Equal(expected, Writes(composer));
        friend.Username = "changed";
        friends[0] = new(friend, false);
        others.Clear();
        Assert.Equal(expected, Writes(composer));
        Assert.Equal(expected, Writes(composer));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void SoundSettingsFreezeVolumesAndPreserveThreeChannelPadding(int count)
    {
        var volumes = Enumerable.Range(1, count).ToList();
        var composer = new SoundSettingsComposer(volumes, true, false, true, 2);
        object[] expected = [count > 0 ? 1 : 0, count > 1 ? 2 : 0, count > 2 ? 3 : 0,
            true, false, true, 2, 0, true, true, true];

        Assert.Equal(expected, Writes(composer));
        volumes.Clear();
        volumes.Add(99);
        Assert.Equal(expected, Writes(composer));
        Assert.Equal(expected, Writes(composer));
    }

    [Fact]
    public void ModerationTopicsFreezeNestedPresetsAndKeepWireOrdering()
    {
        var preset = new ModerationPresetActions(7, 0, "type", "caption", "text", 0, 0, 0, 0, "none");
        var presets = new Dictionary<string, List<ModerationPresetActions>> { ["category"] = [preset] };
        var composer = new CfhTopicsInitComposer(CfhTopicCategorySnapshot.Capture(presets));
        object[] expected = [1, "category", 1, "caption", 7, "type"];
        Assert.Equal(expected, Writes(composer));
        preset.Id = 9;
        preset.Caption = "changed";
        preset.Type = "changed";
        presets["category"].Clear();
        presets.Clear();
        Assert.Equal(expected, Writes(composer));
        Assert.Equal(expected, Writes(composer));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public void AchievementNotificationsFreezeDefinitionAndUserProgress(int userLevel, bool completed)
    {
        var achievement = new Achievement { Id = 7, GroupName = "ACH_TEST", Category = "social" };
        var user = new UserAchievement("ACH_TEST", userLevel, 12);
        var level = new AchievementLevel(2, 5, 10, 20);
        var progressed = new AchievementProgressedComposer(AchievementNotificationSnapshot.CaptureProgress(achievement, 2, level, 3, user));
        var unlocked = new AchievementUnlockedComposer(AchievementUnlockSnapshot.Capture(achievement, 2, 10, 5));
        object[] expectedProgress = [7, 2, "ACH_TEST2", 1, 20, 5, 0, 12, completed, "social", "", 3, 0];
        object[] expectedUnlock = [7, 2, 144, "ACH_TEST2", 10, 5, 0, 10, 21, "ACH_TEST1", "social", true];
        Assert.Equal(expectedProgress, Writes(progressed));
        Assert.Equal(expectedUnlock, Writes(unlocked));
        achievement.Id = 9;
        achievement.GroupName = "ACH_CHANGED";
        achievement.Category = "changed";
        user.Level = 99;
        user.Progress = 99;
        Assert.Equal(expectedProgress, Writes(progressed));
        Assert.Equal(expectedUnlock, Writes(unlocked));
        Assert.Equal(expectedProgress, Writes(progressed));
        Assert.Equal(expectedUnlock, Writes(unlocked));
    }

    [Fact]
    public void GroupPresentationPacketsKeepCapturedGroupAndRequesterFields()
    {
        var group = new Group(7, "Crew", "description", "badge", 42, 1, DateTimeOffset.UnixEpoch,
            0, 3, 4, 0, true, new([1], [], []));
        var user = new Habbo { Id = 1, Username = "Alice", Look = "look" };
        var favourite = new UpdateFavouriteGroupComposer(FavouriteGroupSnapshot.Capture(group, 8));
        var removed = new UpdateFavouriteGroupComposer(FavouriteGroupSnapshot.Capture(null, 8));
        var settings = new GroupFurniSettingsComposer(GroupFurniSettingsSnapshot.Capture(group, 9, user.Id));
        var request = new GroupMembershipRequestedComposer(new(group.Id, 3, user.Id, user.Username, user.Look));
        object[] expectedFavourite = [8, 7, 3, "Crew"];
        object[] expectedRemoved = [8, 0, 3, ""];
        object[] expectedSettings = [9u, 7, "Crew", 42u, true, true];
        object[] expectedRequest = [7, 3, 1, "Alice", "look", ""];
        Assert.Equal(expectedFavourite, Writes(favourite));
        Assert.Equal(expectedRemoved, Writes(removed));
        Assert.Equal(expectedSettings, Writes(settings));
        Assert.Equal(expectedRequest, Writes(request));
        group.Id = 99;
        group.Name = "changed";
        group.RoomId = 99;
        group.ForumEnabled = false;
        user.Id = 99;
        user.Username = "changed";
        user.Look = "changed";
        Assert.Equal(expectedFavourite, Writes(favourite));
        Assert.Equal(expectedRemoved, Writes(removed));
        Assert.Equal(expectedSettings, Writes(settings));
        Assert.Equal(expectedRequest, Writes(request));
    }

    private static List<object> Writes(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);

        return packet.Writes;
    }
}
