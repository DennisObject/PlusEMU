using System.Data;
using System.Reflection;
using System.Text.Json;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Users;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class UpstreamPortTests
{
    [Fact]
    public void GiftStoresBoxThenRibbonAndPaintsBoth()
    {
        var stored = GiftWrap.PresentData("bob", "hi", 4, 9, 12, boxId: 3, ribbonId: 7);
        var fields = stored.Split((char)5);

        Assert.Equal(new[] { "bob", "hi", "4", "9", "12", "3", "7" }, fields);
        Assert.Equal(3007, GiftWrap.Style(stored));
        Assert.Equal(1, GiftWrap.Style(GiftWrap.PresentData("bob", "hi", 4, 9, 12, int.MaxValue, 1)));
        Assert.Equal(1, GiftWrap.Style("short"));
    }

    [Theory]
    [InlineData("Buddy\n1\nffffff", true)]
    [InlineData("bad name\n1\nffffff", false)]
    [InlineData("Buddy\n123\nffffff", false)]
    [InlineData("Buddy\n1\nabc", false)]
    [InlineData("Buddy", false)]
    public void PetGiftAcceptsOnlyACompleteValidPet(string data, bool accepted)
    {
        Assert.Equal(accepted, GiftWrap.PetDataAccepted(data));
    }

    [Fact]
    public void WiredIdAndSkateboardNamesComeFromTheFurnitureRow()
    {
        Assert.Equal(WiredBoxType.None, ItemDataManager.ReadWiredType(DBNull.Value));
        Assert.Equal(WiredBoxType.None, ItemDataManager.ReadWiredType("nope"));
        Assert.Equal(WiredBoxType.EffectMoveAndRotate, ItemDataManager.ReadWiredType(32));
        Assert.Equal(InteractionType.Skateboard, ItemDataManager.ReadInteractionType("sb_rail", "default"));
        Assert.Equal(InteractionType.Skateboard, ItemDataManager.ReadInteractionType("sb_ramp", "default"));
        Assert.Equal(InteractionType.Skateboard, ItemDataManager.ReadInteractionType("sb_block", "default"));
        Assert.Equal(InteractionType.CrackableEgg, ItemDataManager.ReadInteractionType("chair", "crackable_egg"));
        Assert.True((int)InteractionType.Skateboard > (int)InteractionType.Exchange);
    }

    [Fact]
    public void PickedUpSeatReleasesTheAvatarAndARealSeatKeepsIt()
    {
        Assert.False(RoomPosture.ReleaseSit(isSitting: true, hasSitStatus: true, tileHasSeat: true));
        Assert.False(RoomPosture.ReleaseSit(isSitting: true, hasSitStatus: true, tileHasSeat: false));
        Assert.True(RoomPosture.ReleaseSit(isSitting: false, hasSitStatus: true, tileHasSeat: false));
        Assert.False(RoomPosture.ReleaseSit(isSitting: false, hasSitStatus: true, tileHasSeat: true));
        Assert.False(RoomPosture.ReleaseLay(isLying: true, hasLayStatus: true, tileHasBed: true));
        Assert.False(RoomPosture.ReleaseLay(isLying: true, hasLayStatus: true, tileHasBed: false));
        Assert.True(RoomPosture.ReleaseLay(isLying: false, hasLayStatus: true, tileHasBed: false));
    }

    [Fact]
    public void MissingRoomCategoryFallsBackWithoutReadingTheList()
    {
        var access = UserAccess.Create([], [new("navigator.searches.staff", false)], ["navigator.searches.staff"]);
        var category = Category("category", "");
        Assert.Equal(36, RoomCategoryChoice.Resolve(12, null, UserAccess.Empty, 1, 1, applyOwnerRule: true));
        Assert.Equal(12, RoomCategoryChoice.Resolve(12, category, UserAccess.Empty, 9, 9, applyOwnerRule: true));
        Assert.Equal(36, RoomCategoryChoice.Resolve(12, Category("category", "navigator.searches.staff"), UserAccess.Empty, 9, 9, applyOwnerRule: false));
        Assert.Equal(36, RoomCategoryChoice.Resolve(12, Category("query", ""), access, 9, 9, applyOwnerRule: false));
        Assert.Equal(36, RoomCategoryChoice.Resolve(12, Category("category", "navigator.searches.staff"), access, 2, 9, applyOwnerRule: true));
        Assert.Equal(12, RoomCategoryChoice.Resolve(12, Category("category", "navigator.searches.staff"), access, 9, 9, applyOwnerRule: true));
    }

    [Fact]
    public void TradeRequestsAreAllowedUntilAUserTurnsThemOff()
    {
        Assert.True(new Habbo().AllowTradingRequests);
    }

    [Fact]
    public void KickbackPayloadMatchesTheNitroParserAndHeaders()
    {
        var packet = new RecordingPacket();
        new KickbackInfoComposer(new Plus.HabboHotel.Subscriptions.ClubKickback(0, "", 0d, 0, 0, 0, 0, 0, 0)).Compose(packet);
        Assert.Equal(new object[] { 0, "", 0d, 0, 0, 0, 0, 0, 0 }, packet.Writes);
        Assert.Equal(4001u, ServerPacketHeader.KickbackInfoComposer);
        Assert.Equal(4001u, ClientPacketHeader.GetKickbackInfoEvent);
        Assert.Equal(nameof(GetKickbackInfoEvent), typeof(GetKickbackInfoEvent).Name);

        var incoming = HeaderValues(typeof(ClientPacketHeader));
        var outgoing = HeaderValues(typeof(ServerPacketHeader));
        Assert.Equal(incoming.Count, incoming.Distinct().Count());
        Assert.Equal(outgoing.Count, outgoing.Distinct().Count());

        var revisionPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Resources", "Revisions", "1.6.6.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(revisionPath));
        var incomingHeaders = document.RootElement.GetProperty("IncomingHeaders");
        var outgoingHeaders = document.RootElement.GetProperty("OutgoingHeaders");
        Assert.Equal(869u, incomingHeaders.GetProperty("GetKickbackInfoEvent").GetUInt32());
        Assert.Equal(3277u, outgoingHeaders.GetProperty("KickbackInfoComposer").GetUInt32());
        AssertUniquePositiveIds(incomingHeaders);
        AssertUniquePositiveIds(outgoingHeaders);
    }

    [Fact]
    public void SkateboardTrickUsesTheRailJustLeft()
    {
        Assert.False(InteractorSkateboard.TryTrick(null, out _, out _, out _, out _));
        Assert.True(InteractorSkateboard.TryTrick(Rail(2), out var body, out var head, out var lift, out var achievement));
        Assert.Equal(3, body);
        Assert.Equal(3, head);
        Assert.Equal(1, lift);
        Assert.Equal("ACH_SkateBoardJump", achievement);
        Assert.True(InteractorSkateboard.TryTrick(Rail(0), out _, out _, out lift, out achievement));
        Assert.Equal(0, lift);
        Assert.Equal("ACH_SkateBoardSlide", achievement);
        Assert.False(InteractorSkateboard.TryTrick(Rail(4), out _, out _, out _, out _));
    }

    [Fact]
    public void RoomExtraDataKeepsLegacyGiftsAndLoadsCrackableAndBackground()
    {
        var gift = FurniExtraData.Load(Definition(InteractionType.Gift), "1\u0005hi", keepLegacy: true);
        var legacy = Assert.IsType<LegacyDataFormat>(gift);
        Assert.Equal("1\u0005hi", legacy.Data);

        var crackable = Assert.IsType<CrackableDataFormat>(FurniExtraData.Load(Definition(InteractionType.CrackableEgg, modes: 5), "", keepLegacy: true));
        Assert.Equal(5u, crackable.Target);
        var loaded = Assert.IsType<CrackableDataFormat>(FurniExtraData.Load(Definition(InteractionType.CrackableEgg, modes: 5), "closed\n2\n9", keepLegacy: true));
        Assert.Equal(2u, loaded.Hits);
        Assert.Equal(9u, loaded.Target);

        var background = Assert.IsType<MapDataFormat>(FurniExtraData.Load(Definition(InteractionType.Background), "state\t0\timageUrl\thttp://x", keepLegacy: true));
        Assert.Equal("0", background.Data["state"]);
        Assert.Equal("http://x", background.Data["imageUrl"]);
        Assert.Equal("state\t0\nimageUrl\thttp://x", FurniExtraData.Branding(new[] { "imageUrl", "http://x" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "imageUrl", "http://x" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "w", "/camera/not-minted.png" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "url", "https://cdn.example/a.png" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "id", "15" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "clickUrl", "" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "offsetX", "javascript:alert(1)" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "offsetX", "{\"w\":\"/camera/a.png\"}" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { @"\u0069mageUrl", @"\u0068ttps:\u002f\u002fexample.invalid/image.png" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "offsetX", @"\u0068ttps:\u002f\u002fexample.invalid/image.png" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "offsetX", "1\nimageUrl\thttps://example.invalid/image.png" }));
        Assert.True(FurniExtraData.RejectsClientImage(new[] { "offsetX", @"\u0000" }));
        Assert.False(FurniExtraData.RejectsClientImage(new[] { "offsetX", "1", "offsetY", "2", "offsetZ", "0" }));

        var inventory = FurniExtraData.Load(Definition(InteractionType.None), "hello", keepLegacy: false);
        Assert.Same(FurniObjectData.Empty, inventory);
    }

    private static void AssertUniquePositiveIds(JsonElement headers)
    {
        var ids = headers.EnumerateObject().Select(entry => entry.Value.GetUInt32()).Where(id => id > 0).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    private static List<uint> HeaderValues(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Select(field => (uint)field.GetRawConstantValue())
        .Where(value => value > 0)
        .ToList();

    private static SearchResultList Category(string type, string requiredPermission) =>
        new(1, "cat", "cat", "Cat", true, 1, requiredPermission, NavigatorViewMode.Regular, type, "NOTHING", 1);

    private static ItemDefinition Definition(InteractionType type, int modes = 1) => new()
    {
        InteractionType = type,
        Modes = modes,
        ItemName = "",
        PublicName = "",
        VendingIds = new List<int>(),
        AdjustableHeights = new List<double>()
    };

    private static Item Rail(int rotation) => new()
    {
        Rotation = rotation,
        Definition = new ItemDefinition
        {
            ItemName = "sb_rail",
            PublicName = "",
            VendingIds = new List<int>(),
            AdjustableHeights = new List<double>()
        }
    };

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = new();
        public int MessageId
        {
            get; set;
        }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value ?? "");
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
