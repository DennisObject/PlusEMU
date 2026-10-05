using Plus.Communication.Packets;
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

    private static List<object> Writes(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes;
    }
}
