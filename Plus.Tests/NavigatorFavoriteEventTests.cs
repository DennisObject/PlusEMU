using Plus.Communication.Packets.Incoming.Navigator;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;
using Xunit;

namespace Plus.Tests;

public sealed class NavigatorFavoriteEventTests
{
    [Fact]
    public async Task AddDecodesRoomIdAndDelegates()
    {
        var service = new RecordingFavorites();
        await new AddFavouriteRoomEvent(service).Parse(null!, HabbiconTestSupport.Incoming(42));
        Assert.Equal((true, (uint)42), Assert.Single(service.Calls));
    }

    [Fact]
    public async Task RemoveDecodesRoomIdAndDelegates()
    {
        var service = new RecordingFavorites();
        await new RemoveFavouriteRoomEvent(service).Parse(null!, HabbiconTestSupport.Incoming(42));
        Assert.Equal((false, (uint)42), Assert.Single(service.Calls));
    }

    private sealed class RecordingFavorites : INavigatorFavoriteService
    {
        public List<(bool Added, uint RoomId)> Calls { get; } = [];
        public void Add(GameClient session, uint roomId) => Calls.Add((true, roomId));
        public void Remove(GameClient session, uint roomId) => Calls.Add((false, roomId));
    }
}
