using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Data.Moodlight;
using Plus.HabboHotel.Items.Data.Toner;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public class WiredTemporaryPacketGuardTests
{
    private static readonly string[] Handlers =
    [
        "Rooms.Engine.MoveObjectEvent", "Rooms.Engine.MoveWallItemEvent", "Rooms.Engine.PickupObjectEvent",
        "Rooms.Engine.UseFurnitureEvent", "Rooms.Engine.UseWallItemEvent", "Rooms.Furni.CreditFurniRedeemEvent",
        "Rooms.Furni.OpenGiftEvent", "Rooms.Furni.UseSellableClothingEvent", "Rooms.Furni.SaveBrandingItemEvent",
        "Rooms.Furni.SetMannequinFigureEvent", "Rooms.Furni.SetMannequinNameEvent", "Rooms.Furni.UpdateMagicTileEvent",
        "Rooms.Furni.SetTonerEvent", "Rooms.Furni.DiceOffEvent", "Rooms.Furni.ThrowDiceEvent", "Rooms.Furni.OneWayGateEvent",
        "Rooms.Furni.GetGroupFurniSettingsEvent", "FriendFurni.FriendFurniConfirmLockEvent", "Catalog.CheckGnomeNameEvent",
        "Rooms.Furni.Moodlight.MoodlightUpdateEvent", "Rooms.Furni.Moodlight.ToggleMoodlightEvent",
        "Rooms.Furni.Stickys.DeleteStickyNoteEvent", "Rooms.Furni.Stickys.GetStickyNoteEvent", "Rooms.Furni.Stickys.UpdateStickyNoteEvent",
        "Rooms.AI.Pets.Horse.ApplyHorseEffectEvent", "Rooms.Furni.Wired.OpenWiredEvent", "Rooms.Furni.Wired.SaveWiredEffectConfigEvent"
    ];

    public static IEnumerable<object[]> GuardedHandlers() => Handlers.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(GuardedHandlers))]
    public async Task ActualPlayerHandlerRejectsAttachedTemporaryIdentityBeforeMutationOrPersistence(string name)
    {
        var (room, client) = Room();
        // A positive id proves the guard uses the immutable marker, independently of wire sign.
        var item = new Item { Id = 7, IsTemporary = true, UserId = 42, OwnerId = 42, RoomId = 1,
            ExtraData = new LegacyDataFormat { Data = "original" }, Definition = new() { Type = ItemType.Floor, BehaviourData = 100,
                InteractionType = Interaction(name) } };
        item.SetState(1, 2, 3.5, new());
        Floor(room).TryAdd(7, item);
        var permanent = new Item { Id = 8, Definition = new() { Type = ItemType.Floor } };
        Floor(room).TryAdd(8, permanent);
        room.MoodlightData = (MoodlightData)RuntimeHelpers.GetUninitializedObject(typeof(MoodlightData));
        room.MoodlightData.ItemId = 7;
        room.TonerData = (TonerData)RuntimeHelpers.GetUninitializedObject(typeof(TonerData));
        room.TonerData.ItemId = 7;
        var type = typeof(MoveObjectEvent).Assembly.GetType("Plus.Communication.Packets.Incoming." + name)!;
        var constructor = type.GetConstructors().Single();
        var database = EditorTestSupport.UntouchableDatabase();
        var arguments = constructor.GetParameters().Select(parameter => parameter.ParameterType == typeof(ISettingsManager)
            ? (object)new EnabledExchangeSettings()
            : parameter.ParameterType == typeof(Plus.HabboHotel.Catalog.IGnomePackageService)
                ? new Plus.HabboHotel.Catalog.GnomePackageService(new Plus.HabboHotel.Catalog.GnomePackageStore(database, Microsoft.Extensions.Logging.Abstractions.NullLogger<Plus.HabboHotel.Catalog.GnomePackageStore>.Instance), null!, null!, TimeProvider.System)
            : parameter.ParameterType == typeof(IRoomItemPickupService)
                ? new RoomItemPickupService(null!, null!, new RoomItemPickupStore(database))
            : parameter.ParameterType == typeof(IGroupPresentationService)
                ? new GroupPresentationService(null!, null!, null!, null!, null!)
            : parameter.ParameterType == typeof(IFurnitureUseService)
                ? new FurnitureUseService(new FurnitureUseStore(database), null!)
                : parameter.ParameterType == typeof(IGiftOpeningService)
                    ? new GiftOpeningService(new GiftStore(database), null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<GiftOpeningService>.Instance)
                : parameter.ParameterType == typeof(IRoomItemMetadataService)
                    ? new RoomItemMetadataService(new RoomItemMetadataStore(database))
                    : parameter.ParameterType == typeof(IRoomInteractionService)
                        ? new RoomInteractionService(new RoomInteractionStore(database))
                        : parameter.ParameterType == typeof(ILoveLockService)
                            ? new LoveLockService(new LoveLockStore(database), TimeProvider.System)
                            : parameter.ParameterType == typeof(IItemRedemptionService)
                                ? new ItemRedemptionService(new ItemRedemptionStore(database), new EnabledExchangeSettings(), null!)
                                : parameter.ParameterType == typeof(IHorseCustomizationService)
                                    ? new HorseCustomizationService(null!, null!, null!, new HorseCustomizationStore(database),
                                        new PetInformationService(TimeProvider.System))
                                    : null).ToArray();
        var handler = (IPacketEvent)constructor.Invoke(arguments);
        var packet = Packet(name);
        if (handler is RoomPacketEvent roomHandler) await roomHandler.Parse(room, client, packet);
        else await handler.Parse(client, packet);
        Assert.Same(item, room.GetRoomItemHandler().GetItem(7));
        Assert.Same(permanent, room.GetRoomItemHandler().GetItem(8));
        Assert.Equal((1, 2, 3.5, 0), (item.GetX, item.GetY, item.GetZ, item.Rotation));
        Assert.Equal("original", item.LegacyDataString);
        Assert.Equal(10, client.GetHabbo().Credits);
        Assert.False(room.MoodlightData.Enabled);
        Assert.Equal(0, room.TonerData.Enabled);
        // No inventory or database dependency was supplied: reaching either would fail this test.
    }

    [Fact]
    public async Task PermanentHighWireIdRemainsEditableWhileTemporaryItemDoesNot()
    {
        var (room, client) = Room();
        var highId = uint.MaxValue - 1;
        var map = new Gamemap(room, new RoomModel("test", 0, 0, 0, 0, "000\r000\r000", 0, 0, false), TestLogging.Navigation);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        map.GenerateMaps();
        var item = new Item { Id = highId, Definition = new() { Type = ItemType.Floor, InteractionType = InteractionType.Stacktool }, GetZ = 1 };
        Floor(room).TryAdd(highId, item);
        using var stream = PlusMemoryStream.GetStream(); var output = new FlashOutgoingPacket(stream);
        output.WriteUInteger(highId); output.WriteInteger(350);
        await new UpdateMagicTileEvent().Parse(client, new FlashIncomingPacket { Buffer = stream.ToArray().AsMemory(6) });
        Assert.Equal(3.5, item.GetZ);
        Assert.False(item.IsTemporary);
    }

    [Theory]
    [InlineData(true, "-2")]
    [InlineData(false, "4294967294")]
    public void RemoveComposerUsesSignedStringOnlyForTemporaryIdentity(bool temporary, string expected)
    {
        var item = new Item { Id = uint.MaxValue - 1, IsTemporary = temporary };
        using var stream = PlusMemoryStream.GetStream(); var output = new FlashOutgoingPacket(stream);
        new ObjectRemoveComposer(item, 42).Compose(output);
        var input = new FlashIncomingPacket { Buffer = stream.ToArray().AsMemory(6) };
        Assert.Equal(expected, input.ReadString()); Assert.False(input.ReadBool());
        Assert.Equal(42, input.ReadInt()); Assert.Equal(0, input.ReadInt()); Assert.False(input.HasDataRemaining());
    }

    [Fact]
    public void AddAndUpdateComposersKeepNegativeTemporaryIdBits()
    {
        var item = new Item { Id = uint.MaxValue - 1, IsTemporary = true, Definition = new() { Type = ItemType.Floor } };
        foreach (var composer in new IServerPacket[] { new ObjectAddComposer(RoomItemSnapshot.Capture(item)), new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)) })
        {
            using var stream = PlusMemoryStream.GetStream(); var output = new FlashOutgoingPacket(stream);
            composer.Compose(output);
            Assert.Equal(-2, new FlashIncomingPacket { Buffer = stream.ToArray().AsMemory(6) }.ReadInt());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoodlightConfigNeverCreatesOrOpensPersistentStateForTemporaryWallItem(bool existingState)
    {
        var (room, client) = Room();
        var item = new Item { Id = uint.MaxValue - 1, IsTemporary = true, Definition = new() { InteractionType = InteractionType.Moodlight } };
        var wall = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_wallItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        wall.TryAdd(item.Id, item);
        if (existingState)
        {
            room.MoodlightData = (MoodlightData)RuntimeHelpers.GetUninitializedObject(typeof(MoodlightData));
            room.MoodlightData.ItemId = item.Id;
        }
        await new Plus.Communication.Packets.Incoming.Rooms.Furni.Moodlight.GetMoodlightConfigEvent().Parse(room, client, new FlashIncomingPacket());
        if (!existingState) Assert.Null(room.MoodlightData);
        Assert.Same(item, room.GetRoomItemHandler().GetItem(item.Id));
    }

    private static InteractionType Interaction(string name) => name switch
    {
        _ when name.Contains("CreditFurni") => InteractionType.Exchange,
        _ when name.Contains("SellableClothing") => InteractionType.PurchasableClothing,
        _ when name.Contains("FriendFurni") => InteractionType.Lovelock,
        _ when name.Contains("Gnome") => InteractionType.GnomeBox,
        _ when name.Contains("Sticky") => InteractionType.Postit,
        _ when name.Contains("Moodlight") => InteractionType.Moodlight,
        _ when name.Contains("Toner") => InteractionType.Toner,
        _ when name.Contains("OneWayGate") => InteractionType.OneWayGate,
        _ when name.Contains("GetGroupFurni") => InteractionType.GuildGate,
        _ when name.Contains("ApplyHorse") => InteractionType.HorseSaddle1,
        _ when name.Contains("OpenGift") => InteractionType.Gift,
        _ => InteractionType.WiredEffect
    };

    private static FlashIncomingPacket Packet(string name)
    {
        using var stream = PlusMemoryStream.GetStream(); var output = new FlashOutgoingPacket(stream);
        if (name.Contains("PickupObject")) output.WriteInteger(0);
        output.WriteUInteger(7);
        if (name.Contains("Gnome")) output.WriteString("Pixel");
        else if (name.Contains("MoveWall")) output.WriteString(":w=1,1 l=1,1 l");
        else if (name.Contains("UseFurniture")) output.WriteInteger(0);
        else if (name.Contains("SetToner")) { output.WriteInteger(10); output.WriteInteger(20); output.WriteInteger(30); }
        else if (name.Contains("SetMannequinName")) output.WriteString("changed");
        else if (name.Contains("FriendFurni")) output.WriteBoolean(true);
        else if (name.Contains("ApplyHorse")) output.WriteInteger(42);
        else if (name.Contains("GetGroupFurni")) output.WriteInteger(0);
        else if (name.Contains("UpdateMagicTile")) output.WriteInteger(500);
        return new() { Buffer = stream.ToArray().AsMemory(6) };
    }

    private static (Room, FlashGameClient) Room()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1; room.OwnerId = 42; room.OwnerName = "owner"; room.Type = "private";
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        client.SetHabbo(new Habbo { Id = 42, Username = "owner", CurrentRoom = room, Credits = 10,
            Access = EditorTestSupport.Access(["room.item_save_branding_items"]) });
        return (room, client);
    }

    private static ConcurrentDictionary<uint, Item> Floor(Room room) => (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling)
        .GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;

    private sealed class EnabledExchangeSettings : ISettingsManager
    {
        public string TryGetValue(string value) => "1";
        public string? GetOptionalValue(string key) => "1";
        public Task Reload() => throw new NotSupportedException();
    }
}
