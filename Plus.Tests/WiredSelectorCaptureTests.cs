using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;
using Xunit.Abstractions;

namespace Plus.Tests;

public sealed class WiredSelectorCaptureTests(ITestOutputHelper output)
{
    [Fact]
    public void RepresentativeFiringCapturesWorldOnceAndOnlyOpensRequiredVariableSessions()
    {
        var f = new Fixture();
        f.Trigger();

        foreach (var name in WiredSelectorModule.Names) {
            f.Selector(name, Configuration(name));
        }

        foreach (var name in WiredAddonModule.Names) {
            f.Addon(name, Configuration(name));
        }

        var actions = 0;
        f.Action(ctx =>
        {
            actions++;

            for (var i = 0; i < 3; i++) {
                var message = ctx.Policy.FormatText(ctx, "Furniture $(f), avatar $(u)");
                Assert.DoesNotContain("$(", message);
            }

            return true;
        });

        while (f.Furniture.Count < 200) {
            f.Furni();
        }

        for (var i = 1; i <= 200; i++) {
            f.User(i);
        }

        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter) { Actor = f.Users[0], EventItem = f.Furniture[^1] }));
        f.Engine.OnFastCycle();
        Assert.Equal(1, actions);
        Assert.Empty(f.Errors);
        Assert.Equal(1, f.WorldCaptures);
        Assert.Equal(200, f.FurnitureConversions);
        Assert.Equal(200, f.AvatarConversions);
        Assert.Equal(200, f.GroupMembershipReads);
        Assert.Equal(2, f.VariableSessions);
        Assert.Equal(f.VariableSessions, f.DisposedSessions);
        output.WriteLine($"200 furniture / 200 avatars / 20 selectors / 15 addons / 3 messages: world captures={f.WorldCaptures}, furniture conversions={f.FurnitureConversions}, avatar conversions={f.AvatarConversions}, group membership reads={f.GroupMembershipReads}, variable sessions={f.VariableSessions}, disposed={f.DisposedSessions}");
    }

    [Fact]
    public void CachedWorldKeepsSequentialFilterInputsFreshAndStartsNewOnNextFiring()
    {
        var f = new Fixture();
        var first = f.User(1);
        var second = f.User(2);
        f.Trigger();
        f.Selector("wf_slc_users_area", new() { IntParams = [0, 0, 20, 20, 0, 0] });
        f.Selector("wf_slc_users_byname", new() { IntParams = [1, 0], Text = "User1" });
        // An empty filtered pool must stay empty through the next filter, even though its name exists in the cached world.
        f.Selector("wf_slc_users_byname", new() { IntParams = [1, 0], Text = "absent" });
        f.Selector("wf_slc_users_byname", new() { IntParams = [1, 0], Text = "User1" });
        var firings = 0;
        f.Action(ctx =>
        {
            firings++;
            Assert.True(ctx.SelectorKinds.HasFlag(WiredSelectionKind.Users));
            Assert.Empty(ctx.Selected.UserIds);
            Assert.Empty(ctx.Targets.ResolveUsers(ctx, [], WiredSources.Selector));

            return true;
        });
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter) { Actor = first });
        f.Engine.OnFastCycle();
        Assert.Equal(1, f.WorldCaptures);
        f.Users.Remove(first);
        f.User(1); // Same virtual ID, different object identity, different firing.
        second.X = 100;
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter) { Actor = second });
        f.Engine.OnFastCycle();
        Assert.Equal(2, firings);
        Assert.Equal(2, f.WorldCaptures);
        Assert.Empty(f.Errors);
        Assert.Equal(0, f.VariableSessions);
    }

    [Fact]
    public void DeferredFormatterReusesWorldButRevalidatesRemovedAndReusedSourceIdentities()
    {
        var f = new Fixture();
        var first = f.User(1);
        f.Trigger();
        f.Selector("wf_slc_users_area", new() { IntParams = [0, 0, 20, 20, 0, 0] });
        f.Addon("wf_xtra_text_output_username", new() { IntParams = [2, 200], Text = "u\t," });
        var seen = new List<string>();
        f.Action(ctx => { seen.Add(ctx.Policy.FormatText(ctx, "$(u)")); return true; }, delay: 1);
        // The selected target can depart while this actorless firing remains valid.
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Engine.OnFastCycle();
        Assert.Equal(1, f.WorldCaptures);
        f.Users.Remove(first);
        f.User(1);
        f.Now = 500;
        f.Engine.OnFastCycle();
        Assert.Equal("", Assert.Single(seen));
        Assert.Equal(1, f.WorldCaptures);
        Assert.Empty(f.Errors);
        Assert.Equal(0, f.VariableSessions);
    }

    [Fact]
    public void OnlyVariableRemoteChainsAndEnabledMovementOperandsRequireQueries()
    {
        var f = new Fixture();
        f.Trigger();
        var variable = f.Selector("wf_slc_users_with_var", Configuration("wf_slc_users_with_var"), x: 2);
        f.Selector("wf_slc_remote", new() { SelectedItems = [variable.Item.Id] });
        var curve = Configuration("wf_xtra_mov_curve") with { IntParams = [7, 100, 80, 1, 0, 0, 0], Text = "custom:10" };
        f.Addon("wf_xtra_mov_curve", curve);
        var projectile = WiredAddonConfiguration.Normalize("wf_xtra_rotate_to_dir", Configuration("wf_xtra_rotate_to_dir")).IntParams.ToArray();
        projectile[15] = 1; // Preserved variable flag, but literal normal distance mode doesn't read it.
        f.Addon("wf_xtra_rotate_to_dir", new() { IntParams = [.. projectile], Text = "\tcustom:10" });
        f.Action(_ => true);
        f.User(1);
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Engine.OnFastCycle();
        Assert.Equal(2, f.VariableSessions); // Remote variable leaf + enabled curve; detached leaf is not fired.
        Assert.Equal(2, f.DisposedSessions);
        Assert.Equal(1, f.WorldCaptures);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void WalkOnFromSelectorMatchesTheFilledPoolAndKeepsTheTriggeringUserAndFurni()
    {
        var f = new Fixture();
        var user = f.User(1);
        var inside = f.Furni(x: 6);
        var outside = f.Furni(x: 15);
        f.ModernTrigger("wf_trg_walks_on_furni", new() { IntParams = [WiredSources.Selector] });
        f.Selector("wf_slc_furni_area", new() { IntParams = [5, 0, 3, 1, 0, 0] });
        var fired = new List<(uint[] Furni, RoomUser[] Users)>();
        f.Action(ctx =>
        {
            fired.Add((ctx.Triggering.FurniIds.ToArray(), ctx.Targets.ResolveUsers(ctx, [], WiredSources.Trigger)));

            return true;
        });

        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.WalkOn) { Actor = user, EventItem = inside }));
        f.Engine.OnFastCycle();
        var (furni, users) = Assert.Single(fired);
        Assert.Equal([inside.Id], furni);
        Assert.Same(user, Assert.Single(users));

        Assert.False(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.WalkOn) { Actor = user, EventItem = outside }));
        f.Engine.OnFastCycle();
        Assert.Single(fired);
        // The rejected firing left nothing pending: the next matching walk still fires.
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.WalkOn) { Actor = user, EventItem = inside }));
        f.Engine.OnFastCycle();
        Assert.Equal(2, fired.Count);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void WalkOnFromPicksStillRejectsOtherFurniBeforeRunningSelectors()
    {
        var f = new Fixture();
        var user = f.User(1);
        var picked = f.Furni(x: 6);
        var other = f.Furni(x: 7);
        f.ModernTrigger("wf_trg_walks_on_furni", new() { IntParams = [WiredSources.Selected], SelectedItems = [picked.Id] });
        f.Selector("wf_slc_furni_area", new() { IntParams = [5, 0, 3, 1, 0, 0] });
        var fired = 0;
        f.Action(_ => { fired++; return true; });

        Assert.False(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.WalkOn) { Actor = user, EventItem = other }));
        f.Engine.OnFastCycle();
        Assert.Equal(0, f.WorldCaptures);
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.WalkOn) { Actor = user, EventItem = picked }));
        f.Engine.OnFastCycle();
        Assert.Equal(1, fired);
        Assert.Empty(f.Errors);
    }

    private static WiredConfiguration Configuration(string name) => name switch
    {
        "wf_slc_users_area" or "wf_slc_furni_area" => new() { IntParams = [0, 0, 20, 20, 0, 0] },
        "wf_slc_users_byname" => new() { Text = "User1" },
        "wf_slc_users_with_var" => new() { Text = "custom:10\t" },
        "wf_slc_furni_with_var" => new() { Text = "custom:11\t" },
        "wf_xtra_execution_limit" => new() { IntParams = [100, 1000] },
        "wf_xtra_text_output_furni_name" => new() { IntParams = [2, 200], Text = "f\t," },
        "wf_xtra_text_output_username" => new() { IntParams = [2, 200], Text = "u\t," },
        "wf_xtra_mov_curve" => new() { IntParams = [7, 100, 80, 0, 0, 0, 0] },
        _ => new()
    };

    // The real dispatcher, resolver, boxes, variable sessions and formatters execute against typed room query inputs.
    // The room constructor's SQL/network globals are deliberately outside this operation-count fixture.
    private sealed class Fixture : IWiredRuntimeOperations, IWiredVariableDirectory
    {
        public readonly Room Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        public readonly List<Item> Furniture = [];
        public readonly List<RoomUser> Users = [];
        public readonly List<Exception> Errors = [];
        private readonly WiredSelectorRoomState _state = new();
        private readonly Dictionary<uint, IWiredConfiguredItem> _boxes = [];
        private readonly WiredVariableModule _variables;
        private readonly FixtureClock _clock;
        public readonly WiredStackEngine Engine;
        public long Now;
        public int WorldCaptures, FurnitureConversions, AvatarConversions, GroupMembershipReads, VariableSessions, DisposedSessions;
        private uint _next;

        private readonly ConcurrentDictionary<uint, Item> _placed;

        public Fixture()
        {
            Room.Id = 1;
            // Native pick validation reads the live room, so the fixture room holds a real item handler.
            const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
            var handler = new RoomItemHandling(Room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
            typeof(Room).GetField("_roomItemHandling", Private)!.SetValue(Room, handler);
            _placed = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", Private)!.GetValue(handler)!;
            _clock = new(this);
            _variables = new(1, this, new MemoryWiredVariableStore(), _clock);
            Engine = new(() => Now, box => Furniture.Contains(box.Item), _ => true, _ => { }, Errors.Add);
            Engine.BindRuntime(Room, new(() => Furniture, () => Users,
                id => Furniture.FirstOrDefault(x => x.Id == id), id => Users.FirstOrDefault(x => x.VirtualId == id)), this);
        }
        private sealed class FixtureClock(Fixture fixture) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(fixture.Now);
        }
        public Item Furni(string name = "", int x = 0)
        {
            var item = new Item
            {
                Id = ++_next,
                GetX = x,
                GetZ = _next,
                OwnerId = 5,
                Definition = new() { Id = 1, InteractionName = name, ItemName = "test", PublicName = "Furniture", VendingIds = [], AdjustableHeights = [] }
            };
            Furniture.Add(item);
            _placed[item.Id] = item;
            typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, Room);

            return item;
        }
        public RoomUser User(int id)
        {
            var user = new RoomUser(id + 100, 1, id, Room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
            Users.Add(user);

            return user;
        }
        public void Trigger() => Add(new Trigger { Item = Furni(), Instance = Room });
        public void ModernTrigger(string name, WiredConfiguration c)
        {
            Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
            var box = new WiredModernTrigger(Room, Furni(name), descriptor);
            Configure(box, c);
            Add(box);
        }
        public void Action(Func<WiredRuntimeContext, bool> body, int delay = 0) => Add(new Box(WiredBoxCategory.Action)
        { Item = Furni(), Instance = Room, Body = body, Configuration = new() { Delay = delay } });
        public IWiredContextualSelector Selector(string name, WiredConfiguration c, int x = 0)
        {
            var box = WiredSelectorFactory.Create(Room, Furni(name, x), _state, TestGroupManager.Empty, Queries, ReadWorld)!;
            Configure(box, c);
            Add(box);

            return box;
        }
        public void Addon(string name, WiredConfiguration c)
        {
            var box = WiredAddonFactory.Create(Room, Furni(name), _state, TestGroupManager.Empty, Queries, ReadWorld)!;
            Configure(box, c);
            Add(box);
        }
        private static void Configure(IWiredConfiguredItem box, WiredConfiguration c)
        {
            // Variable-family cards and blank drafts install from native records; the rest map their runtime draft.
            if (WiredNativeAuxiliaryEditor.Supports(box.Descriptor.CanonicalName)
                || box.Descriptor.Category is WiredBoxCategory.Selector or WiredBoxCategory.Addon && c.IntParams.IsEmpty && c.Text.Length == 0) {
                WiredNativeTestSupport.Install(box, AuxNative(box.Descriptor, c));

                return;
            }

            Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, c, out var normalized, out var error), error);
            box.ApplyConfiguration(normalized);
        }
        // The variable-family selectors and addons are installed from their native editor records.
        private static WiredNativeEditorConfiguration AuxNative(WiredBoxDescriptor descriptor, WiredConfiguration c)
        {
            var native = WiredNativeEditorProjection.DefaultNative(descriptor) with
            {
                PrimaryItems = [.. c.SelectedItems.Select(id => new WiredNativeItemReference(id, false))]
            };
            var air = WiredNativeAuxiliaryEditor.AirTarget(0);

            return descriptor.CanonicalName switch
            {
                "wf_slc_users_with_var" => native with { VariableIds = ["user:10", "n"] },
                "wf_slc_furni_with_var" => native with { VariableIds = ["furni:11", "n"] },
                "wf_xtra_execution_limit" => native with { OwnedIntParams = [100, 2] },
                "wf_xtra_text_output_furni_name" => native with { OwnedIntParams = [1], FurniSourceTypes = [200], Text = "f\t," },
                "wf_xtra_text_output_username" => native with { OwnedIntParams = [1], UserSourceTypes = [200], Text = "u\t," },
                // Jump strength read from a user variable.
                "wf_xtra_mov_curve" when c.VariableIds.Length != 0 || c.Text.Length != 0 =>
                    native with { OwnedIntParams = [1, 80, air], UserSourceTypes = [0], FurniSourceTypes = [0], VariableIds = ["user:10"] },
                // A distance variable is chosen but the literal distance mode never reads it.
                "wf_xtra_rotate_to_dir" when c.Text.Length != 0 =>
                    native with { OwnedIntParams = native.OwnedIntParams.SetItem(15, 1), VariableIds = ["n", "user:10"] },
                _ => native
            };
        }
        private void Add(IWiredConfiguredItem box)
        {
            _boxes[box.Item.Id] = box;
            Engine.Add(box);
        }
        private WiredSelectorVariableQueries Queries(WiredRuntimeContext ctx)
        {
            VariableSessions++;
            var session = WiredSelectorVariableBridge.Create(ctx, _variables);

            return session with { DisposeSession = () => { DisposedSessions++; session.Dispose(); } };
        }
        private WiredSelectorWorld ReadWorld(WiredRuntimeContext ctx)
        {
            WorldCaptures++;
            var items = ctx.Targets.ResolveFurni(ctx, [], WiredSources.AllRoom, raw: true);
            var users = ctx.Targets.ResolveUsers(ctx, [], WiredSources.AllRoom, raw: true);
            var furni = items.Select(item =>
            {
                FurnitureConversions++;

                return new WiredSelectorFurniture(item.Id, (int)item.Definition.Id, item.Definition.PublicName,
                    item.LegacyDataString, item.GetX, item.GetY, item.GetZ, 1, [(item.GetX, item.GetY)], IsWired: _boxes.ContainsKey(item.Id));
            }).ToArray();
            var avatars = users.Select(user =>
            {
                AvatarConversions++;
                GroupMembershipReads++;

                return new WiredSelectorAvatar(user.VirtualId, $"User{user.VirtualId}", WiredSelectorEntityKind.Player,
                    user.X, user.Y, GroupIds: new HashSet<int> { 1 });
            }).ToArray();
            var remotes = _boxes.Values.Where(box => box.Descriptor.Category == WiredBoxCategory.Selector)
                .ToDictionary(box => box.Item.Id, box => new WiredRemoteSelector(box.Descriptor.CanonicalName, ctx.ConfigurationOf(box)));

            return new(20, 20, furni, avatars, 1, remotes);
        }
        public WiredVariableDefinition? Find(uint id) => id is 10 or 11 ? new(id, 1, 5, "test",
            id == 10 ? WiredVariableTarget.User : WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
        public bool CallStacks(WiredRuntimeContext ctx, IEnumerable<Item> targets, bool negative = false) => Engine.CallStacks(ctx, targets, negative);
        public bool SendSignal(WiredRuntimeContext ctx, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => Engine.SendSignal(ctx, receivers, selection, negative);
        public void ResetTimers(IEnumerable<Item> targets) => Engine.ResetTimers(targets);
    }

    private class Box(WiredBoxCategory category) : IWiredContextualAction
    {
        public Room Instance { get; set; } = null!;
        public Item Item { get; set; } = null!;
        public WiredBoxType Type => WiredBoxType.None;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public WiredBoxDescriptor Descriptor { get; } = new("test", category, 0, "test") { Support = WiredBoxSupport.Implemented };
        public WiredConfiguration Configuration { get; set; } = new();
        public Func<WiredRuntimeContext, bool> Body { get; set; } = _ => true;
        public bool IsNegative => false;
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments) => throw new InvalidOperationException("Modern box requires context");
        public bool Execute(WiredRuntimeContext context) => Body(context);
        public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        {
            validated = proposed;
            error = "";

            return true;
        }
        public void ApplyConfiguration(WiredConfiguration validated) => Configuration = validated;
    }
    private sealed class Trigger() : Box(WiredBoxCategory.Trigger), IWiredContextualTrigger
    {
        public IReadOnlyCollection<WiredEventKind> Events { get; } = [WiredEventKind.Enter];
        public bool HidesChat(WiredRuntimeContext context) => false;
    }
}
