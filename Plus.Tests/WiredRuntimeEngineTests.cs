using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public class WiredRuntimeEngineTests
{
    [Fact]
    public void SelectorsPrecedePoliciesConditionsAndActionsAndEmptyDoesNotFallback()
    {
        var f = new Fixture();
        var actor = f.User(10);
        var eventItem = f.Furni();
        var trace = new List<string>();
        f.Trigger();
        f.Add(new Selector { SelectBody = ctx =>
        {
            trace.Add("selector");
            return new(new([], [actor.VirtualId]), WiredSelectionKind.Both);
        }});
        f.Add(new Addon { ApplyBody = ctx =>
        {
            trace.Add("addon");
            Assert.True(ctx.SelectorKinds.HasFlag(WiredSelectionKind.Furni));
            Assert.Empty(ctx.Selected.FurniIds);
            Assert.Equal(new[] { actor.VirtualId }, ctx.Selected.UserIds);
            return true;
        }});
        f.Add(new Box(WiredBoxCategory.Condition) { Body = ctx => { trace.Add("condition"); return true; }});
        f.Action(ctx =>
        {
            trace.Add("action");
            Assert.Empty(ctx.Targets.ResolveFurni(ctx, [], WiredSources.Selector));
            Assert.Equal(new[] { eventItem }, ctx.Targets.ResolveFurni(ctx, [], WiredSources.Trigger));
            return true;
        });

        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter) { Actor = actor, EventItem = eventItem }));
        Assert.Equal(new[] { "selector", "addon", "condition", "action" }, trace);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void FilterAndInvertApplyOnceAndPreserveOtherTargetKind()
    {
        var f = new Fixture();
        var first = f.Furni();
        var second = f.Furni();
        var user = f.User(7);
        f.Trigger();
        f.Add(new Selector { SelectBody = _ => new(new([first.Id, second.Id], [user.VirtualId]), WiredSelectionKind.Both) });
        f.Add(new Selector { SelectBody = _ => new(new([first.Id]), WiredSelectionKind.Furni, true, true) });
        f.Action(ctx =>
        {
            Assert.Equal(new[] { second.Id }, ctx.Selected.FurniIds);
            Assert.Equal(new[] { user.VirtualId }, ctx.Selected.UserIds);
            return true;
        });
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TriggerChoosesNegativeBranchOnlyForFailedConditions(bool passes)
    {
        var f = new Fixture();
        f.Trigger();
        f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => passes });
        var positive = f.Action();
        var negative = f.Action(negative: true);
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(passes ? 1 : 0, positive.Calls);
        Assert.Equal(passes ? 0 : 1, negative.Calls);
    }

    [Fact]
    public void NegativeCallRunsPositiveActionsOnFailedTargetAndForwardsMultipleItems()
    {
        var f = new Fixture();
        var first = f.Furni();
        var second = f.Furni();
        f.Trigger();
        f.Add(new Selector { SelectBody = _ => new(new([first.Id, second.Id]), WiredSelectionKind.Furni) });
        var target = f.Trigger(x: 2);
        f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => false }, x: 2);
        var called = f.Action(ctx =>
        {
            Assert.Equal(new[] { first.Id, second.Id }.Order(), ctx.Targets.ResolveFurni(ctx, [], WiredSources.Trigger).Select(x => x.Id).Order());
            Assert.Equal(1, ctx.Depth);
            return true;
        }, x: 2);
        var excluded = f.Action(negative: true, x: 2);
        f.Action(ctx => ctx.Operations.CallStacks(ctx, [target.Item], negative: true));
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        // The target trigger also receives Enter; its failing condition runs its negative branch once.
        Assert.Equal(1, called.Calls);
        Assert.Equal(1, excluded.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void SpeechConsumptionIsSynchronousAndRequiresAcceptedHideTrigger()
    {
        var f = new Fixture();
        var trigger = f.Trigger(WiredEventKind.Speech);
        trigger.Hide = true;
        var condition = f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => false });
        var action = f.Action(delay: 2);
        Assert.False(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Speech) { Message = "hello" }));
        condition.Body = _ => true;
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Speech) { Message = "hello" }));
        Assert.Equal(0, action.Calls);
        f.Advance(1000);
        Assert.Equal(1, action.Calls);
        trigger.Hide = false;
        Assert.False(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Speech) { Message = "hello" }));
    }

    [Fact]
    public void IndependentHalfSecondDelaysAndGlobalPriorityAreShared()
    {
        var f = new Fixture();
        f.Trigger();
        var trace = new List<string>();
        var late = f.Action(_ => { trace.Add("late"); return true; }, delay: 2);
        var one = f.Action(_ => { trace.Add("one"); return false; }, delay: 1);
        var two = f.Action(_ => { trace.Add("two"); return true; }, delay: 1);
        one.Item.GetZ = 3;
        two.Item.GetZ = 4;
        f.Action(_ => { trace.Add("immediate"); return true; });
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(new[] { "immediate" }, trace);
        f.Advance(499); Assert.Single(trace);
        f.Advance(1); Assert.Equal(new[] { "immediate", "one", "two" }, trace);
        f.Advance(500); Assert.Equal(new[] { "immediate", "one", "two", "late" }, trace);
        Assert.False(f.Engine.NeedsFastCycle);
    }

    [Fact]
    public void ContextCapturesConfigurationAndDurablePublicationCancelsOriginalFiring()
    {
        var f = new Fixture();
        f.Trigger();
        var seen = new List<string>();
        Box? action = null;
        action = f.Action(ctx => { seen.Add(ctx.ConfigurationOf(action!).Text); return true; }, delay: 1);
        action.ApplyConfiguration(action.Configuration with { Text = "old" });
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        // Direct changes demonstrate snapshot isolation; production saves use the publisher below.
        action.ApplyConfiguration(action.Configuration with { Text = "unpublished" });
        f.Advance(500);
        Assert.Equal(new[] { "old" }, seen);
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        Assert.Throws<InvalidOperationException>(() => f.Engine.PublishConfigured(action,
            action.Configuration with { Text = "failed" }, () => throw new InvalidOperationException("database")));
        Assert.Equal("unpublished", action.Configuration.Text);
        Assert.True(f.Engine.PublishConfigured(action, action.Configuration with { Text = "saved" }, () =>
            Assert.Equal("unpublished", action.Configuration.Text)));
        f.Advance(500);
        Assert.Single(seen);
        Assert.Equal("saved", action.Configuration.Text);
    }

    [Fact]
    public void DetachedVisitCannotAffectReusedAvatarButLeaveRoomActionsStillRun()
    {
        var f = new Fixture();
        var departed = f.User(3);
        f.Trigger(WiredEventKind.Leave);
        var resolved = new List<RoomUser[]>();
        var action = f.Action(ctx =>
        {
            resolved.Add(ctx.Targets.ResolveUsers(ctx, [], WiredSources.Trigger));
            return true;
        }, delay: 1);
        f.Users.Remove(departed);
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Leave) { Actor = departed }));
        var replacement = f.User(3);
        f.Advance(500);
        Assert.Equal(1, action.Calls);
        Assert.Empty(Assert.Single(resolved));
        Assert.NotSame(departed, replacement);
    }

    [Fact]
    public void SourceCapsCachePerFiringAndRawUniverseRemainsComplete()
    {
        var f = new Fixture();
        var items = Enumerable.Range(0, 5).Select(_ => f.Furni()).ToArray();
        f.Trigger();
        f.Add(new Addon { ApplyBody = ctx => { ctx.Policy.Addons.FurniLimit = 2; return true; } });
        f.Action(ctx =>
        {
            var picked = ctx.Targets.ResolveFurni(ctx, items.Select(x => x.Id), WiredSources.Selected);
            Assert.Equal(2, picked.Length);
            Assert.Equal(picked, ctx.Targets.ResolveFurni(ctx, items.Select(x => x.Id).Reverse(), WiredSources.Selected));
            Assert.Equal(5, ctx.Targets.ResolveFurni(ctx, items.Select(x => x.Id), WiredSources.Selected, raw: true).Length);
            f.Furniture.Remove(picked[0]);
            Assert.Single(ctx.Targets.ResolveFurni(ctx, items.Select(x => x.Id), WiredSources.Selected));
            return true;
        });
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void QuotaAndStatefulPickerRunAfterSuccessfulConditionOnly()
    {
        var f = new Fixture();
        f.Trigger();
        var condition = f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => false });
        var calls = 0;
        var picker = new Picker();
        f.Add(new Addon { ApplyBody = ctx => { ctx.Policy.Addons.ActionPicker = picker; return true; } });
        f.Add(new Addon { AfterConditions = true, ApplyBody = _ => { calls++; return true; } });
        var action = f.Action();
        Assert.False(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(0, calls); Assert.Equal(0, picker.Calls);
        condition.Body = _ => true;
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(1, calls); Assert.Equal(1, picker.Calls); Assert.Equal(1, action.Calls);
    }

    [Fact]
    public void ScopedConditionPolicyKeepsUngroupedConditionsRequired()
    {
        var f = new Fixture();
        f.Trigger();
        var good = f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => true });
        var bad = f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => false });
        var ordinary = f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => false });
        f.Add(new Addon { ApplyBody = ctx =>
        {
            ctx.Policy.Addons.Conditions = new(WiredConditionEvaluation.Any, WiredSources.Selected, 0, new HashSet<uint> { good.Item.Id, bad.Item.Id });
            return true;
        }});
        var action = f.Action();
        Assert.False(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        ordinary.Body = _ => true;
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(1, action.Calls);
    }

    [Fact]
    public void RecursiveCallsAreBoundedByActualEngineDepth()
    {
        var f = new Fixture(new() { MaxDepth = 3 });
        var trigger = f.Trigger();
        var action = f.Action(ctx => ctx.Operations.CallStacks(ctx, [trigger.Item]));
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(4, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void SignalsCopyPayloadRevalidateReceiversAndUseSameScheduler()
    {
        var f = new Fixture();
        var item = f.Furni();
        var antenna = f.Furni("antenna", x: 2);
        f.Trigger();
        f.Trigger(WiredEventKind.Signal, x: 2);
        var receiver = f.Action(ctx =>
        {
            Assert.Equal(new[] { item }, ctx.Targets.ResolveFurni(ctx, [], WiredSources.Signal));
            Assert.Equal(17, ctx.Values["value"]);
            Assert.Equal(1, ctx.Depth);
            return true;
        }, x: 2);
        f.Action(ctx =>
        {
            ctx.Values["value"] = 17;
            var selection = new WiredSelection([item.Id]);
            var accepted = ctx.Operations.SendSignal(ctx, [antenna, antenna], selection);
            selection.FurniIds.Clear();
            ctx.Values["value"] = 99;
            return accepted;
        });
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        Assert.Equal(0, receiver.Calls);
        f.Engine.OnFastCycle();
        Assert.Equal(1, receiver.Calls);
        Assert.Empty(f.Errors);
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        antenna.SetState(3, 0, 0, []); antenna.SetState(2, 0, 0, []);
        f.Engine.OnFastCycle();
        Assert.Equal(1, receiver.Calls);
    }

    [Fact]
    public void DispatchCapturesRoomIdentityOnceForMultipleTriggers()
    {
        var f = new Fixture();
        f.Trigger(); f.Trigger(); f.Trigger();
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        Assert.Equal(1, f.FurnitureReads);
        Assert.Equal(1, f.UserReads);
    }

    [Fact]
    public void EmptyFastPassDoesNotReadRoomStateAndTimedBoxesPollAtFiftyMs()
    {
        var f = new Fixture();
        for (var i = 0; i < 10000; i++) f.Engine.OnFastCycle();
        Assert.Equal(0, f.FurnitureReads); Assert.Equal(0, f.UserReads);
        var timer = f.Add(new Timer());
        var action = f.Action();
        Assert.True(f.Engine.NeedsFastCycle);
        f.Engine.OnFastCycle();
        Assert.Equal(1, timer.Polls); Assert.Equal(1, action.Calls);
        f.Advance(49); Assert.Equal(1, timer.Polls);
        f.Advance(1); Assert.Equal(2, timer.Polls); Assert.Equal(2, action.Calls);
        f.Engine.OnCycle(); Assert.Equal(2, timer.Polls);
        f.Engine.Remove(timer.Item.Id); Assert.False(f.Engine.NeedsFastCycle);
    }

    [Fact]
    public void PromotionPersistsBeforeReplacingLegacyReferenceAndCancelsCapturedWork()
    {
        var f = new Fixture();
        f.Trigger();
        var original = f.Action(delay: 1);
        var candidate = new Box(WiredBoxCategory.Action) { Item = original.Item, Instance = f.Room };
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        Assert.Throws<InvalidOperationException>(() => f.Engine.PublishPromotion(original, candidate, new(),
            () => throw new InvalidOperationException("database")));
        Assert.True(f.Engine.TryGet(original.Item.Id, out var stillOriginal)); Assert.Same(original, stillOriginal);
        Assert.True(f.Engine.PublishPromotion(original, candidate, new() { Text = "saved" }, () =>
        {
            Assert.True(f.Engine.TryGet(original.Item.Id, out var persisted)); Assert.Same(original, persisted);
        }));
        Assert.True(f.Engine.TryGet(original.Item.Id, out var promoted)); Assert.Same(candidate, promoted);
        f.Advance(500); Assert.Equal(0, original.Calls);
        Assert.Equal("saved", candidate.Configuration.Text);
    }

    [Fact]
    public void AuxiliaryAnimationWorkSharesBudgetDelayAndConfigurationCancellation()
    {
        var f = new Fixture();
        f.Trigger();
        var calls = 0;
        var action = f.Action(ctx => f.Engine.ScheduleAux(ctx, 400, () => calls++));
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(399); Assert.Equal(0, calls);
        f.Advance(1); Assert.Equal(1, calls);
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Engine.PublishConfigured(action, action.Configuration, () => { });
        f.Advance(400); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalDelayedActorFiringCancelsAfterDepartureOrNewVisit(bool reenter)
    {
        var f = new Fixture();
        var actor = f.User(7);
        f.Trigger(WiredEventKind.Speech);
        var action = f.Action(delay: 1);
        f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Speech) { Actor = actor });
        f.Users.Remove(actor);
        if (reenter) f.User(7); // Same stable user, a new RoomUser visit object.
        f.Advance(500);
        Assert.Equal(0, action.Calls);
    }

    [Fact]
    public void LegacyPeriodicMixedStackUsesAnyActorPerContextualConditionAndActorlessActions()
    {
        var f = new Fixture();
        var alice = f.User(1);
        var bob = f.User(2);
        var repeater = new LegacyRepeater { Item = f.Furni(), Instance = f.Room };
        repeater.Item.Definition.InteractionType = InteractionType.WiredTrigger;
        f.Engine.Add(repeater);
        f.Add(new Box(WiredBoxCategory.Condition) { Body = ctx => ReferenceEquals(ctx.Event.Actor, alice) });
        f.Add(new Box(WiredBoxCategory.Condition) { Body = ctx => ReferenceEquals(ctx.Event.Actor, bob) });
        var action = f.Action(ctx => { Assert.Null(ctx.Event.Actor); return true; });
        Assert.True(f.Engine.RunPeriodicStack(repeater, [[alice], [bob]]));
        Assert.Equal(1, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void PendingCapIncludesQueuedSignalsBeforeNestedActionsAndAuxiliaryWork()
    {
        var f = new Fixture(new() { MaxPendingStacks = 2 });
        var antenna = f.Furni("antenna", x: 3);
        f.Trigger();
        var target = f.Trigger(WiredEventKind.Signal, x: 2);
        var nested = f.Action(x: 2);
        var aux = 0;
        f.Action(ctx =>
        {
            Assert.True(f.Engine.SendSignal(ctx, [antenna], new()));
            Assert.False(f.Engine.CallStacks(ctx, [target.Item]));
            Assert.False(f.Engine.ScheduleAux(ctx, 10, () => aux++));
            return true;
        });
        Assert.True(f.Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(0, nested.Calls);
        f.Advance(50);
        Assert.Equal(0, aux);
        Assert.Empty(f.Errors);
    }

    private sealed class LegacyRepeater : IWiredItem, IWiredCycle
    {
        public Room Instance { get; set; } = null!;
        public Item Item { get; set; } = null!;
        public WiredBoxType Type => WiredBoxType.TriggerRepeat;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public int Delay { get; set; }
        public int TickCount { get; set; }
        public bool OnCycle() => true;
        public bool Execute(params object[] arguments) => true;
        public void HandleSave(IIncomingPacket packet) { }
    }

    private sealed class Picker : IWiredActionPicker
    {
        public int Calls;
        public IReadOnlyList<uint> Pick(IReadOnlyList<uint> actionIds) { Calls++; return actionIds; }
        public void Reset() { }
    }

    private sealed class Fixture : IWiredRuntimeOperations
    {
        public readonly Room Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        public readonly List<Item> Furniture = [];
        public readonly List<RoomUser> Users = [];
        public readonly List<Exception> Errors = [];
        public WiredStackEngine Engine { get; }
        public long Now;
        public int FurnitureReads, UserReads;
        private uint next;
        public Fixture(WiredEngineLimits? limits = null)
        {
            Engine = new(() => Now, box => Furniture.Contains(box.Item), _ => true, _ => { }, Errors.Add, limits);
            Engine.BindRuntime(Room, new(() => { FurnitureReads++; return Furniture; }, () => { UserReads++; return Users; },
                id => Furniture.FirstOrDefault(x => x.Id == id), id => Users.FirstOrDefault(x => x.VirtualId == id)), this);
        }
        public Item Furni(string interaction = "", int x = 0)
        {
            var item = new Item { Id = ++next, GetX = x, Definition = new() { InteractionName = interaction, ItemName = "test", PublicName = "test", VendingIds = [], AdjustableHeights = [] } };
            Furniture.Add(item); return item;
        }
        public RoomUser User(int id) { var user = new RoomUser(id + 100, 0, id, Room); Users.Add(user); return user; }
        public T Add<T>(T box, int x = 0) where T : Box
        {
            box.Instance = Room; box.Item = Furni(x: x); box.Item.GetZ = next;
            Engine.Add(box); return box;
        }
        public Trigger Trigger(WiredEventKind kind = WiredEventKind.Enter, int x = 0) => Add(new Trigger(kind), x);
        public Box Action(Func<WiredRuntimeContext, bool>? body = null, int delay = 0, bool negative = false, int x = 0) =>
            Add(new Box(WiredBoxCategory.Action) { Body = body ?? (_ => true), Configuration = new() { Delay = delay }, IsNegative = negative }, x);
        public void Advance(int milliseconds) { Now += milliseconds; Engine.OnFastCycle(); }
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
        public WiredBoxDescriptor Descriptor { get; } = new("test", category, 0, 0, "test") { Support = WiredBoxSupport.Implemented };
        public WiredConfiguration Configuration { get; set; } = new();
        public Func<WiredRuntimeContext, bool> Body { get; set; } = _ => true;
        public bool IsNegative { get; set; }
        public int Calls;
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments) => throw new InvalidOperationException("Modern box requires context");
        public bool Execute(WiredRuntimeContext context) { Calls++; return Body(context); }
        public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        { validated = proposed; error = ""; return true; }
        public void ApplyConfiguration(WiredConfiguration validated) => Configuration = validated;
    }
    private class Trigger(WiredEventKind kind) : Box(WiredBoxCategory.Trigger), IWiredContextualTrigger
    {
        public IReadOnlyCollection<WiredEventKind> Events { get; } = [kind];
        public bool Hide;
        public bool HidesChat(WiredRuntimeContext context) => Hide;
    }
    private sealed class Timer() : Trigger(WiredEventKind.Periodic), IWiredTimedTrigger
    {
        public int Polls;
        public WiredRuntimeEvent? Poll(long nowMilliseconds) { Polls++; return new(WiredEventKind.Periodic) { EventItem = Item }; }
        public void Reset(long nowMilliseconds) { }
    }
    private sealed class Selector() : Box(WiredBoxCategory.Selector), IWiredContextualSelector
    {
        public Func<WiredRuntimeContext, WiredSelectorResult> SelectBody { get; set; } = _ => new(new(), WiredSelectionKind.Both);
        public WiredSelectorResult Select(WiredRuntimeContext context) => SelectBody(context);
    }
    private sealed class Addon() : Box(WiredBoxCategory.Addon), IWiredContextualAddon
    {
        public bool AfterConditions { get; set; }
        public Func<WiredRuntimeContext, bool> ApplyBody { get; set; } = _ => true;
        public bool Apply(WiredRuntimeContext context) => ApplyBody(context);
        public void Reset() { }
    }
}
