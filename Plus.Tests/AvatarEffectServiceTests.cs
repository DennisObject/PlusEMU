using System.Buffers.Binary;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Inventory.AvatarEffect;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Xunit;

namespace Plus.Tests;

public class AvatarEffectServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public async Task ActivationPersistsBeforeTheModelAndPacketChange()
    {
        var store = new RecordingStore();
        var (habbo, effect) = Owner(sprite: 42, duration: 3600, activated: false);
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        store.OnActivate = _ =>
        {
            Assert.False(effect.Activated);
            Assert.Empty(sent);
        };

        await Activated(store).Parse(client, Packet(42));

        Assert.Equal(new[] { (effect.Id, Now) }, store.Activations);
        Assert.True(effect.Activated);
        Assert.Equal(Now, effect.ActivatedAt);
        var message = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.AvatarEffectActivatedComposer, message.Header);
    }

    [Fact]
    public async Task ActivatedWireCarriesSpriteDurationAndNotPermanent()
    {
        var store = new RecordingStore();
        var (habbo, _) = Owner(sprite: 42, duration: 3600, activated: false);
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Activated(store).Parse(client, Packet(42));

        Assert.Equal(new object[] { 42, 3600, false }, Writes(new AvatarEffectActivatedComposer(new AvatarEffectActivation(42, 3600))));
        Assert.Equal(3, Writes(new AvatarEffectActivatedComposer(new AvatarEffectActivation(42, 3600))).Count);
        Assert.Single(sent);
    }

    [Fact]
    public async Task StoreFailureLeavesTheEffectAndThePacketUntouched()
    {
        var store = new RecordingStore { Fail = true };
        var (habbo, effect) = Owner(sprite: 42, duration: 3600, activated: false);
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Activated(store).Parse(client, Packet(42)));

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
        var (habbo, _) = Owner(sprite: 42, duration: 3600, activated: reason == "already-active", activatedAt: Now.AddSeconds(-10));
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Activated(store).Parse(client, Packet(reason == "not-owned" ? 99 : 42));

        Assert.Empty(store.Activations);
        Assert.Empty(sent);
    }

    [Fact]
    public void ExpiredWireCarriesOnlyTheSprite()
    {
        Assert.Equal(new object[] { 42 }, Writes(new AvatarEffectExpiredComposer(new AvatarEffectExpiry(42))));
    }

    [Fact]
    public void ExpiryModelSendsTheSpriteAndDecrementsQuantity()
    {
        var store = new RecordingStore();
        var (habbo, effect) = Owner(sprite: 42, duration: 10, activated: true, activatedAt: Now.AddSeconds(-20), quantity: 2, store: store);
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;

        habbo.Effects.CheckEffectExpiry(habbo);

        Assert.Equal(1, effect.Quantity);
        Assert.False(effect.Activated);
        var message = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.AvatarEffectExpiredComposer, message.Header);
        Assert.Equal(42, BinaryPrimitives.ReadInt32BigEndian(message.Payload));
    }

    [Fact]
    public void EffectsWireCarriesQuantityAndRemainingTimeForActiveAndIdleEffects()
    {
        var active = new AvatarEffectEntry(1, 3600, 2, true, 3000);
        var idle = new AvatarEffectEntry(2, 60, 1, false, -1);

        Assert.Equal(new List<object> { 1, 0, 3600, 2, 3000, false }, Writes(new AvatarEffectsComposer([active])).Skip(1).ToList());
        Assert.Equal(new List<object> { 2, 0, 60, 1, -1, false }, Writes(new AvatarEffectsComposer([idle])).Skip(1).ToList());
        Assert.Equal(1, Writes(new AvatarEffectsComposer([active])).First());
    }

    [Fact]
    public void CapturedRemainingTimeFollowsTheInjectedClockAtTheBoundary()
    {
        var (habbo, _) = Owner(sprite: 1, duration: 100, activated: true, activatedAt: Now.AddSeconds(-100));
        var service = new AvatarEffectService(new RecordingStore(), new FixedClock(Now));
        Assert.Equal(0, service.Capture(habbo).Single().RemainingSeconds);

        habbo.Effects.GetAllEffects.Single().ActivatedAt = Now.AddSeconds(-99);
        Assert.Equal(1, service.Capture(habbo).Single().RemainingSeconds);

        habbo.Effects.GetAllEffects.Single().ActivatedAt = Now.AddSeconds(-150);
        Assert.Equal(0, service.Capture(habbo).Single().RemainingSeconds);

        habbo.Effects.GetAllEffects.Single().Activated = false;
        Assert.Equal(-1, service.Capture(habbo).Single().RemainingSeconds);
    }

    [Fact]
    public void CapturedEffectsDoNotFollowLaterMutationOfTheModel()
    {
        var (habbo, effect) = Owner(sprite: 42, duration: 3600, activated: true, activatedAt: Now.AddSeconds(-600), quantity: 2);
        var service = new AvatarEffectService(new RecordingStore(), new FixedClock(Now));
        var captured = service.Capture(habbo);
        var before = Writes(new AvatarEffectsComposer(captured));

        effect.Quantity = 9;
        effect.Activated = false;
        effect.Duration = 1;

        Assert.Equal(before, Writes(new AvatarEffectsComposer(captured)));
        Assert.Equal(1, captured.Single().Quantity);
    }

    [Fact]
    public async Task NegativeSelectionAndNoRoomAreSilentNoOps()
    {
        var (habbo, _) = Owner(sprite: 42, duration: 3600, activated: true, activatedAt: Now.AddSeconds(-10));
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await new AvatarEffectSelectedEvent(new AvatarEffectService(new RecordingStore(), new FixedClock(Now))).Parse(client, Packet(-5));
        await new AvatarEffectSelectedEvent(new AvatarEffectService(new RecordingStore(), new FixedClock(Now))).Parse(client, Packet(42));

        Assert.Empty(sent);
    }

    private static Plus.Communication.Packets.Incoming.Inventory.AvatarEffect.AvatarEffectActivatedEvent Activated(IAvatarEffectStore store) =>
        new(new AvatarEffectService(store, new FixedClock(Now)));

    private static (Habbo Habbo, AvatarEffect Effect) Owner(int sprite, double duration, bool activated, DateTimeOffset? activatedAt = null, int quantity = 1,
        IAvatarEffectStore? store = null)
    {
        var habbo = new Habbo { Id = 7, Username = "owner" };
        habbo.Effects = new EffectsComponent();
        habbo.Effects.Init(habbo);
        var effect = new AvatarEffect(501, 7, sprite, duration, activated, activatedAt, quantity, store);
        habbo.Effects.TryAdd(effect);
        return (habbo, effect);
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
        public bool Fail { get; set; }
        public Action<int>? OnActivate { get; set; }

        public IReadOnlyList<AvatarEffect> Load(int userId) => [];
        public AvatarEffect Create(int userId, int spriteId, double duration) => throw new NotSupportedException();

        public void Activate(int id, DateTimeOffset timestamp)
        {
            OnActivate?.Invoke(id);
            if (Fail) throw new InvalidOperationException("forced store failure");
            Activations.Add((id, timestamp));
        }

        public void SaveQuantity(int id, int quantity, bool activated, DateTimeOffset? activatedAt) { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
