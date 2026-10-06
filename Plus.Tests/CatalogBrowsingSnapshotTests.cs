using System.Diagnostics.CodeAnalysis;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogBrowsingSnapshotTests
{
    [Fact]
    public void PetPalettesKeepTheirLegacyFieldsAndFreezeRaceData()
    {
        var race = new PetRace(1, 2, 3, true, true);
        var source = new List<PetRace> { race };
        var composer = new SellablePetBreedsComposer(PetPaletteSnapshot.Capture("pet", 7, source));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[] { "pet", 1, 7, 2, 3, true, false, false }, before.Writes);
        race.PrimaryColour = 99;
        race.SecondaryColour = 99;
        source.Clear();
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [Fact]
    public void PromotableRoomsUseInjectedOwnerLoaderAndOneCapturedExpiryInstant()
    {
        var now = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new CountingClock(now);
        var exact = new RoomData { Id = 2, Name = "expired", Promotion = new("p", "d", 0, now.AddHours(-1), now, new ThrowingClock()) };
        var loader = new Rooms
        {
            Data = [new() { Id = 1, Name = "available" }, exact,
                new() { Id = 3, Name = "active", Promotion = new("p", "d", 0, now, now.AddMinutes(1), new ThrowingClock()) }]
        };
        var service = new CatalogBrowsingService(null!, null!, loader, clock);
        var composer = new PromotableRoomsComposer(service.CapturePromotableRooms(42));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[] { true, 2, 1u, "available", false, 2u, "expired", false }, before.Writes);
        Assert.Equal(42, loader.OwnerId);
        Assert.Equal(1, clock.Reads);
        exact.Name = "changed";
        loader.Data.Clear();
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [Fact]
    public async Task BrowsingHandlersOnlyDecodeAndDelegate()
    {
        var service = new RecordingBrowsing();
        await new GetSellablePetPalettesEvent(service).Parse(null!, HabbiconTestSupport.Incoming("pet"));
        await new GetPromotableRoomsEvent(service).Parse(null!, null!);
        Assert.Equal("pet", service.Type);
        Assert.True(service.PromotableRequested);
    }

    private sealed class Rooms : IRoomDataLoader
    {
        public List<RoomData> Data { get; set; } = [];
        public int OwnerId { get; private set; }
        public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId) { OwnerId = ownerId; return Data; }
        public bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data) => throw new NotSupportedException();
    }
    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return now; }
    }
    private sealed class ThrowingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Expiry must use the captured clock.");
    }
    private sealed class RecordingBrowsing : ICatalogBrowsingService
    {
        public string? Type { get; private set; }
        public bool PromotableRequested { get; private set; }
        public void ShowPetPalettes(GameClient session, string type) => Type = type;
        public void ShowPromotableRooms(GameClient session) => PromotableRequested = true;
    }
}
