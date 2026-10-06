using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Inventory.AvatarEffect;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Xunit;

namespace Plus.Tests;

public class AvatarEffectServiceTests
{
    private static readonly DateTimeOffset Now = FixedTimeProvider.Epoch;

    [Fact]
    public async Task ActivationPersistsBeforeTheModelAndPacketChange()
    {
        var store = new RecordingStore();
        var (habbo, effect) = Owner(sprite: 42, duration: 3600, activated: false, store: store, clock: new FixedTimeProvider(Now));
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        store.OnActivate = _ =>
        {
            Assert.False(effect.Activated);
            Assert.Empty(sent);
        };

        await Activated(Now).Parse(client, Packet(42));

        Assert.Equal(new[] { (effect.Id, Now) }, store.Activations);
        Assert.True(effect.Activated);
        Assert.Equal(Now, effect.ActivatedAt);
        Assert.Equal(ServerPacketHeader.AvatarEffectActivatedComposer, Assert.Single(sent).Header);
    }

    [Fact]
    public void ActivatedWireCarriesSpriteDurationAndNotPermanent()
    {
        Assert.Equal(new object[] { 42, 3600, false }, Writes(new AvatarEffectActivatedComposer(new AvatarEffectActivation(42, 3600))));
    }

    [Fact]
    public async Task StoreFailureLeavesTheEffectAndThePacketUntouched()
    {
        var store = new RecordingStore { Fail = true };
        var (habbo, effect) = Owner(sprite: 42, duration: 3600, activated: false, store: store, clock: new FixedTimeProvider(Now));
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Activated(Now).Parse(client, Packet(42)));

        Assert.False(effect.Activated);
        Assert.Null(effect.ActivatedAt);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData("already-active")]
    [InlineData("not-owned")]
    public async Task AlreadyActiveOrUnownedEffectsAreIgnored(string reason)
    {
        var store = new RecordingStore();
        var (habbo, _) = Owner(sprite: 42, duration: 3600, activated: reason == "already-active", activatedAt: Now.AddSeconds(-10), store: store, clock: new FixedTimeProvider(Now));
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Activated(Now).Parse(client, Packet(reason == "not-owned" ? 99 : 42));

        Assert.Empty(store.Activations);
        Assert.Empty(sent);
    }

    [Fact]
    public void ExpiredWireCarriesOnlyTheSprite()
    {
        Assert.Equal(new object[] { 42 }, Writes(new AvatarEffectExpiredComposer(new AvatarEffectExpiry(42))));
    }

    [Fact]
    public void ExpiryPersistsTheLowerQuantityAndThenSendsTheSprite()
    {
        var store = new RecordingStore();
        var (habbo, effect) = Owner(sprite: 42, duration: 10, activated: true, activatedAt: Now.AddSeconds(-20), quantity: 2, store: store, clock: new FixedTimeProvider(Now));
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        store.OnSave = _ => Assert.Equal(2, effect.Quantity);

        habbo.Effects.CheckEffectExpiry(habbo);

        Assert.Equal(new[] { (effect.Id, 1, false) }, store.Saves);
        Assert.Equal(1, effect.Quantity);
        Assert.False(effect.Activated);
        var message = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.AvatarEffectExpiredComposer, message.Header);
        Assert.Equal(42, BinaryPrimitives.ReadInt32BigEndian(message.Payload));
    }

    [Fact]
    public void ExpiryStoreFailureLeavesTheModelAndThePacketUnchanged()
    {
        var store = new RecordingStore { FailSave = true };
        var (habbo, effect) = Owner(sprite: 42, duration: 10, activated: true, activatedAt: Now.AddSeconds(-20), quantity: 2, store: store, clock: new FixedTimeProvider(Now));
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        Assert.Throws<InvalidOperationException>(() => habbo.Effects.CheckEffectExpiry(habbo));

        Assert.Equal(2, effect.Quantity);
        Assert.True(effect.Activated);
        Assert.Equal(Now.AddSeconds(-20), effect.ActivatedAt);
        Assert.Empty(sent);
    }

    [Fact]
    public void EffectsWireCarriesQuantityAndRemainingTimeForActiveAndIdleEffects()
    {
        Assert.Equal(new List<object> { 1, 0, 3600, 2, 3000, false }, Writes(new AvatarEffectsComposer([new AvatarEffectEntry(1, 3600, 2, true, 3000)])).Skip(1).ToList());
        Assert.Equal(new List<object> { 2, 0, 60, 1, -1, false }, Writes(new AvatarEffectsComposer([new AvatarEffectEntry(2, 60, 1, false, -1)])).Skip(1).ToList());
        Assert.Equal(1, Writes(new AvatarEffectsComposer([new AvatarEffectEntry(1, 3600, 2, true, 3000)])).First());
    }

    [Fact]
    public void CapturedRemainingTimeFollowsTheInjectedClockAtTheBoundary()
    {
        var clock = new MutableClock(Now);
        var (habbo, effect) = Owner(sprite: 1, duration: 100, activated: true, activatedAt: Now.AddSeconds(-100), clock: clock);
        var service = new AvatarEffectService(clock);
        Assert.Equal(0, service.Capture(habbo).Single().RemainingSeconds);

        effect.ActivatedAt = Now.AddSeconds(-99);
        Assert.Equal(1, service.Capture(habbo).Single().RemainingSeconds);

        effect.ActivatedAt = Now.AddSeconds(-150);
        Assert.Equal(0, service.Capture(habbo).Single().RemainingSeconds);

        effect.Activated = false;
        Assert.Equal(-1, service.Capture(habbo).Single().RemainingSeconds);
    }

    [Fact]
    public void GatingUsesTheSameInstantAtAFutureClock()
    {
        var clock = new MutableClock(Now);
        var (habbo, _) = Owner(sprite: 42, duration: 100, activated: true, activatedAt: Now.AddSeconds(-50), clock: clock);

        Assert.True(habbo.Effects.HasEffect(42, true));
        clock.Now = Now.AddSeconds(60);
        Assert.False(habbo.Effects.HasEffect(42, true));
        Assert.True(habbo.Effects.HasEffectAt(42, Now.AddSeconds(49), true));
        Assert.False(habbo.Effects.HasEffectAt(42, Now.AddSeconds(50), true));
    }

    [Fact]
    public void CapturedEffectsDoNotFollowLaterMutationOfTheModel()
    {
        var (habbo, effect) = Owner(sprite: 42, duration: 3600, activated: true, activatedAt: Now.AddSeconds(-600), quantity: 2, clock: new FixedTimeProvider(Now));
        var captured = new AvatarEffectService(new FixedTimeProvider(Now)).Capture(habbo);
        var before = Writes(new AvatarEffectsComposer(captured));

        effect.Quantity = 9;
        effect.Activated = false;
        effect.Duration = 1;

        Assert.Equal(before, Writes(new AvatarEffectsComposer(captured)));
        Assert.Equal(1, captured.Single().Quantity);
    }

    [Fact]
    public async Task RoomSelectionAppliesOnlyAnActiveOwnedEffect()
    {
        var clock = new FixedTimeProvider(Now);
        var (habbo, _) = Owner(sprite: 42, duration: 3600, activated: true, activatedAt: Now.AddSeconds(-10), clock: clock);
        var (client, _) = InRoom(habbo);

        await new AvatarEffectSelectedEvent(new AvatarEffectService(clock)).Parse(client, Packet(42));

        Assert.Equal(42, habbo.Effects.CurrentEffect);
    }

    [Fact]
    public async Task RoomSelectionIgnoresAnExpiredEffectAtTheDeadline()
    {
        var clock = new FixedTimeProvider(Now);
        var (habbo, _) = Owner(sprite: 42, duration: 10, activated: true, activatedAt: Now.AddSeconds(-10), clock: clock);
        var (client, _) = InRoom(habbo);

        await new AvatarEffectSelectedEvent(new AvatarEffectService(clock)).Parse(client, Packet(42));

        Assert.Equal(0, habbo.Effects.CurrentEffect);
    }

    [Fact]
    public async Task RoomSelectionOfANegativeIdIsANoOp()
    {
        var clock = new FixedTimeProvider(Now);
        var (habbo, _) = Owner(sprite: 42, duration: 3600, activated: true, activatedAt: Now.AddSeconds(-10), clock: clock);
        var (client, _) = InRoom(habbo);

        await new AvatarEffectSelectedEvent(new AvatarEffectService(clock)).Parse(client, Packet(-5));

        Assert.Equal(0, habbo.Effects.CurrentEffect);
    }

    [Fact]
    public async Task ActivatedEventDecodesOneIntAndSendsOnlyTheActivation()
    {
        var store = new RecordingStore();
        var (habbo, _) = Owner(sprite: 42, duration: 60, activated: false, store: store, clock: new FixedTimeProvider(Now));
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Activated(Now).Parse(client, Packet(42));

        var message = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.AvatarEffectActivatedComposer, message.Header);
        Assert.Equal(42, BinaryPrimitives.ReadInt32BigEndian(message.Payload));
        Assert.Equal(60, BinaryPrimitives.ReadInt32BigEndian(message.Payload.AsSpan(4)));
    }

    [Fact]
    public void FutureOrMissingTimestampsCountAsNoTimeUsedAndNeverExceedTheDuration()
    {
        var clock = new FixedTimeProvider(Now);
        var (habbo, _) = Owner(sprite: 1, duration: 100, activated: true, activatedAt: Now.AddSeconds(30), clock: clock);
        var service = new AvatarEffectService(clock);
        Assert.Equal(100, service.Capture(habbo).Single().RemainingSeconds);

        habbo.Effects.GetAllEffects.Single().ActivatedAt = null;
        Assert.Equal(100, service.Capture(habbo).Single().RemainingSeconds);
    }

    [Fact]
    public void HugeDurationsStayInsideTheWireIntegerRange()
    {
        var clock = new FixedTimeProvider(Now);
        var (habbo, _) = Owner(sprite: 1, duration: 3_000_000_000d, activated: true, activatedAt: Now, clock: clock);

        var entry = new AvatarEffectService(clock).Capture(habbo).Single();

        Assert.Equal(int.MaxValue, entry.Duration);
        Assert.Equal(int.MaxValue, entry.RemainingSeconds);
    }

    [Fact]
    public async Task LastQuantityExpiryRemovesTheEffectAndDeniesReactivation()
    {
        var store = new RecordingStore();
        var clock = new FixedTimeProvider(Now);
        var (habbo, effect) = Owner(sprite: 42, duration: 10, activated: true, activatedAt: Now.AddSeconds(-20), quantity: 1, store: store, clock: clock);
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;

        habbo.Effects.CheckEffectExpiry(habbo);

        Assert.Equal(new[] { (effect.Id, 0, false) }, store.Saves);
        Assert.Empty(habbo.Effects.GetAllEffects);
        Assert.False(habbo.Effects.HasEffect(42));

        sent.Clear();
        store.Activations.Clear();
        await Activated(Now).Parse(client, Packet(42));

        Assert.Empty(store.Activations);
        Assert.Empty(sent);
    }

    [Fact]
    public void FailedLastQuantityExpiryKeepsTheEffectInTheComponent()
    {
        var store = new RecordingStore { FailSave = true };
        var clock = new FixedTimeProvider(Now);
        var (habbo, effect) = Owner(sprite: 42, duration: 10, activated: true, activatedAt: Now.AddSeconds(-20), quantity: 1, store: store, clock: clock);
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;

        Assert.Throws<InvalidOperationException>(() => habbo.Effects.CheckEffectExpiry(habbo));

        Assert.Same(effect, Assert.Single(habbo.Effects.GetAllEffects));
        Assert.Equal(1, effect.Quantity);
        Assert.True(effect.Activated);
        Assert.Empty(sent);
    }

    private static AvatarEffectActivatedEvent Activated(DateTimeOffset clock) => new(new AvatarEffectService(new FixedTimeProvider(clock)));

    private static (Habbo Habbo, AvatarEffect Effect) Owner(int sprite, double duration, bool activated, DateTimeOffset? activatedAt = null, int quantity = 1,
        IAvatarEffectStore? store = null, TimeProvider? clock = null)
    {
        var habbo = new Habbo { Id = 7, Username = "owner" };
        habbo.Effects = new EffectsComponent(clock ?? new FixedTimeProvider(Now));
        habbo.Effects.Init(habbo);
        var effect = new AvatarEffect(501, 7, sprite, duration, activated, activatedAt, quantity, store ?? new RecordingStore());
        habbo.Effects.TryAdd(effect);
        return (habbo, effect);
    }

    // A room with one registered user whose client is the owner, so selection reaches the effect component.
    private static (GameClient Client, RoomUser User) InRoom(Habbo habbo)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, new FixedTimeProvider(Now), new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
        var (client, _) = HabbiconTestSupport.Client(habbo);
        habbo.CurrentRoom = room;
        var user = new RoomUser(7, room.Id, 1, room, client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(room.GetRoomUserManager())!;
        users.TryAdd(7, user);
        return (client, user);
    }

    private static List<object> Writes(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes;
    }

    private static FlashIncomingPacket Packet(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return new FlashIncomingPacket { Buffer = bytes };
    }

    private sealed class RecordingStore : IAvatarEffectStore
    {
        public List<(int Id, DateTimeOffset At)> Activations { get; } = new();
        public List<(int Id, int Quantity, bool Activated)> Saves { get; } = new();
        public bool Fail { get; set; }
        public bool FailSave { get; set; }
        public Action<int>? OnActivate { get; set; }
        public Action<int>? OnSave { get; set; }

        public IReadOnlyList<AvatarEffect> Load(int userId) => [];
        public AvatarEffect Create(int userId, int spriteId, double duration) => throw new NotSupportedException();

        public void Activate(int id, DateTimeOffset timestamp)
        {
            OnActivate?.Invoke(id);
            if (Fail) throw new InvalidOperationException("forced store failure");
            Activations.Add((id, timestamp));
        }

        public void SaveQuantity(int id, int quantity, bool activated, DateTimeOffset? activatedAt)
        {
            OnSave?.Invoke(quantity);
            if (FailSave) throw new InvalidOperationException("forced save failure");
            Saves.Add((id, quantity, activated));
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
