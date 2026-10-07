using System.Collections.Immutable;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Polls;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Polls;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Polls;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void RoomPollRequiresTheServedSurveyAndExactAdmittedRoomAndCancellationStopsAnswers()
    {
        var actor = Viewer();
        var poll = RoomPollTestData.Poll(_room.RoomId);
        var calls = new List<string>();
        var store = Proxy<IRoomPollStore>((method, args) =>
        {
            calls.Add(method);
            return method switch
            {
                nameof(IRoomPollStore.Load) => poll,
                nameof(IRoomPollStore.Completed) => false,
                nameof(IRoomPollStore.Answer) => true,
                _ => throw new InvalidOperationException(method)
            };
        });
        var polls = new RoomPollService(store, TimeProvider.System);
        polls.Offer(_client);
        Assert.Equal(ServerPacketHeader.PollOfferComposer, _client.Sent.Last());
        polls.Answer(_client, 10, 1, ["1"]);
        Assert.DoesNotContain(nameof(IRoomPollStore.Answer), calls);
        polls.Start(_client, 99);
        Assert.DoesNotContain(ServerPacketHeader.PollContentsComposer, _client.Sent);
        polls.Start(_client, 10);
        Assert.Equal(ServerPacketHeader.PollContentsComposer, _client.Sent.Last());
        polls.Answer(_client, 10, 1, ["1"]);
        Assert.Equal(1, calls.Count(call => call == nameof(IRoomPollStore.Answer)));
        polls.Reject(_client, 99);
        polls.Answer(_client, 10, 1, ["1"]);
        Assert.Equal(2, calls.Count(call => call == nameof(IRoomPollStore.Answer)));
        polls.Reject(_client, 10);
        polls.Answer(_client, 10, 1, ["1"]);
        Assert.Equal(2, calls.Count(call => call == nameof(IRoomPollStore.Answer)));
        polls.Start(_client, 10);
        _client.GetHabbo().CurrentRoom = null;
        polls.Answer(_client, 10, 1, ["1"]);
        Assert.Equal(2, calls.Count(call => call == nameof(IRoomPollStore.Answer)));
    }

    [Fact]
    public void CompletedRoomPollIsNotOfferedOrStartedAgain()
    {
        Viewer();
        var poll = RoomPollTestData.Poll(_room.RoomId);
        var store = Proxy<IRoomPollStore>((method, args) => method switch
        {
            nameof(IRoomPollStore.Load) => poll,
            nameof(IRoomPollStore.Completed) => true,
            _ => throw new InvalidOperationException(method)
        });
        var polls = new RoomPollService(store, TimeProvider.System);
        polls.Offer(_client);
        polls.Start(_client, 10);
        polls.Answer(_client, 10, 1, ["1"]);
        Assert.DoesNotContain(ServerPacketHeader.PollOfferComposer, _client.Sent);
        Assert.DoesNotContain(ServerPacketHeader.PollContentsComposer, _client.Sent);
    }

    [Fact]
    public async Task PollAnswerHandlerDecodesEveryDistinctStringAndBoundsTheCount()
    {
        string[]? actual = null;
        var calls = 0;
        var polls = Proxy<IRoomPollService>((method, args) =>
        {
            Assert.Equal(nameof(IRoomPollService.Answer), method);
            Assert.Same(_client, args[0]);
            Assert.Equal(10, args[1]);
            Assert.Equal(2, args[2]);
            actual = (string[])args[3]!;
            calls++;
            return null;
        });
        var handler = new PollAnswerEvent(polls);
        var packet = HabbiconTestSupport.Incoming(10, 2, 2, "10", "20");
        await handler.Parse(_client, packet);
        Assert.Equal(new[] { "10", "20" }, actual);
        Assert.False(packet.HasDataRemaining());
        await handler.Parse(_client, HabbiconTestSupport.Incoming(10, 2, 65));
        await handler.Parse(_client, HabbiconTestSupport.Incoming(10, 2, -1));
        Assert.Equal(1, calls);
    }
}

public sealed class RoomPollPacketTests
{
    [Fact]
    public void ContentsMatchesAirAndV75RootChildChoiceAndTrailingNpsFieldOrder()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new PollContentsComposer(RoomPollTestData.Poll(42)).Compose(packet);
        Assert.Equal(new object[]
        {
            10, "Survey", "Thanks", 2,
            1, 1, 1, "Recommend?", 0, 0, 2, "1", "Yes", 1, "0", "No", 0, 1,
            3, 1, 3, "Why?", 1, 0, 0,
            2, 2, 2, "Select", 0, 0, 2, "10", "One", 0, "20", "Two", 0, 0, true
        }, packet.Writes);
        var offer = new HabbiconTestSupport.RecordingPacket();
        new PollOfferComposer(RoomPollTestData.Poll(42)).Compose(offer);
        Assert.Equal(new object[] { 10, "CLIENT_NPS", "Survey", "Opinion" }, offer.Writes);
    }

    [Fact]
    public void ConditionalCompletionRequiresOnlyTheSelectedFollowupAndRejectsForeignChoices()
    {
        var poll = RoomPollTestData.Poll(42);
        var answers = new Dictionary<int, string[]> { [1] = ["1"], [2] = ["10", "20"] };
        Assert.False(poll.IsComplete(answers));
        Assert.True(poll.IsAvailable(poll.Find(3)!, answers));
        answers[3] = ["Good"];
        Assert.True(poll.IsComplete(answers));
        answers[1] = ["0"];
        Assert.False(poll.IsAvailable(poll.Find(3)!, answers));
        Assert.True(poll.IsComplete(answers));
        Assert.False(RoomPollSnapshot.ValidAnswer(poll.Find(1)!, ["crafted"]));
        Assert.False(RoomPollSnapshot.ValidAnswer(poll.Find(1)!, ["1", "0"]));
        Assert.False(RoomPollSnapshot.ValidAnswer(poll.Find(2)!, ["10", "10"]));
        Assert.True(RoomPollSnapshot.ValidAnswer(poll.Find(2)!, []));
    }
}

internal static class RoomPollTestData
{
    public static RoomPollSnapshot Poll(uint roomId) => new(10, roomId, "CLIENT_NPS", "Survey", "Opinion", "Thanks", true,
    [
        new(1, 1, 1, "Recommend?", 0, 0, [new("1", "Yes", 1), new("0", "No", 0)],
            [new(3, 1, 3, "Why?", 1, 0, [], [])]),
        new(2, 2, 2, "Select", 0, 0, [new("10", "One", 0), new("20", "Two", 0)], [])
    ]);
}
