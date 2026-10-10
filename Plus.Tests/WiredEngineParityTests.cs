using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.Games.Teams;
using Xunit;

namespace Plus.Tests;

public partial class WiredRuntimeEngineTests
{
    [Fact]
    public void SignalReachesOffTileReceiverPerMatchingAntenna()
    {
        var f = new Fixture();
        var first = f.Furni("antenna", x: 2);
        var second = f.Furni("antenna", x: 3);
        var forwarded = f.Furni();
        var user = f.User(1);
        f.Trigger();
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [first.Id, second.Id] }, x: 5);
        var receiver = f.Action(ctx =>
        {
            Assert.Empty(ctx.Triggering.FurniIds);
            Assert.Equal(new[] { user }, ctx.Targets.ResolveUsers(ctx, [], WiredSources.Trigger));
            Assert.Equal(new[] { user }, ctx.Targets.ResolveUsers(ctx, [], WiredSources.Signal));
            Assert.Equal(new[] { forwarded }, ctx.Targets.ResolveFurni(ctx, [], WiredSources.Signal));

            return true;
        }, x: 5);
        f.Action(ctx => f.Engine.SendSignal(ctx, [first, second, first], new([forwarded.Id], [user.VirtualId])));

        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter) { Actor = user }));
        f.Advance(50);
        Assert.Equal(2, receiver.Calls);
        f.Advance(50);
        Assert.Equal(2, receiver.Calls);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignalKeepsOriginalTriggeringActorAndFurnitureSeparateFromForwardedSelection(bool forwardUser)
    {
        var f = new Fixture();
        var antenna = f.Furni("antenna", x: 2);
        var clicked = f.Furni();
        var substituted = f.Furni();
        var user = f.User(1);
        f.Trigger(WiredEventKind.ClickFurni);
        f.Add(new Selector { SelectBody = _ => new(new([substituted.Id]), WiredSelectionKind.Furni) });
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [antenna.Id] }, x: 5);
        var receiver = f.Action(ctx =>
        {
            Assert.Same(user, ctx.Event.Actor);
            Assert.Same(clicked, ctx.Event.EventItem);
            Assert.Equal(new[] { clicked }, ctx.Targets.ResolveFurni(ctx, [], WiredSources.Trigger));
            Assert.Equal(new[] { user }, ctx.Targets.ResolveUsers(ctx, [], WiredSources.Trigger));
            Assert.Equal(new[] { substituted }, ctx.Targets.ResolveFurni(ctx, [], WiredSources.Signal));
            Assert.Equal(forwardUser ? new[] { user } : [], ctx.Targets.ResolveUsers(ctx, [], WiredSources.Signal));

            return true;
        }, x: 5);
        f.Action(ctx => f.Engine.SendSignal(ctx, [antenna], new([substituted.Id], forwardUser ? [user.VirtualId] : [])));
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.ClickFurni) { Actor = user, EventItem = clicked });
        f.Advance(50);
        f.Advance(50);
        Assert.Equal(1, receiver.Calls);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public void SignalsFireOncePerAddressedAntennaThatTheReceiverPicks(bool sendBoth, bool receiveBoth, int expected)
    {
        var f = new Fixture();
        var first = f.Furni("antenna", x: 2);
        var second = f.Furni("antenna", x: 3);
        f.Trigger();
        ModernTrigger(f, "wf_trg_recv_signal", new()
        {
            IntParams = [0, 100],
            SelectedItems = receiveBoth ? [first.Id, second.Id] : [first.Id]
        }, x: 5);
        var receiver = f.Action(x: 5);
        f.Action(ctx => f.Engine.SendSignal(ctx, sendBoth ? [first, second] : [first], new()));
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        f.Advance(50);
        Assert.Equal(expected, receiver.Calls);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SignalReceiverUsesItsOwnNormalConditionGateForBothSenderBranches(bool negativeSender, bool receiverPasses)
    {
        var f = new Fixture();
        var antenna = f.Furni("antenna", x: 2);
        f.Trigger();
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [antenna.Id] }, x: 5);
        f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => receiverPasses }, x: 5);
        var receiver = f.Action(x: 5);
        f.Action(ctx => f.Engine.SendSignal(ctx, [antenna], new(), negativeSender));
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        f.Advance(50);
        Assert.Equal(receiverPasses ? 1 : 0, receiver.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void SignalContinuationRunsAfterTheSenderSliceWithoutAnExtraCycleAndKeepsPositiveWaits()
    {
        var f = new Fixture();
        var antenna = f.Furni("antenna", x: 2);
        f.Trigger();
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [antenna.Id] }, x: 5);
        var trace = new List<string>();
        f.Add(new Addon { ApplyBody = context => { context.Policy.Addons.ExecuteInOrder = true; return true; } });
        var receiver = f.Action(_ => { trace.Add("receiver"); return true; }, x: 5);
        var delayed = f.Action(delay: 1, x: 5);
        f.Action(ctx => { trace.Add("send"); return f.Engine.SendSignal(ctx, [antenna], new()); });
        f.Action(_ => { trace.Add("sender-end"); return true; });
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        Assert.Equal(new[] { "send", "sender-end", "receiver" }, trace);
        Assert.Equal(1, receiver.Calls);
        f.Advance(499);
        Assert.Equal(0, delayed.Calls);
        f.Advance(1);
        Assert.Equal(1, delayed.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void SignalContinuationWaitsForAnEarlierExternalEnvelopeWithoutGivingItsEffectsEarlyEligibility()
    {
        var f = new Fixture();
        var antenna = f.Furni("antenna", x: 2);
        f.Trigger(WiredEventKind.Speech);
        f.Trigger(WiredEventKind.Enter, x: 3);
        var external = f.Action(x: 3);
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [antenna.Id] }, x: 5);
        var receiver = f.Action(x: 5);
        f.Action(ctx => f.Engine.SendSignal(ctx, [antenna], new()));
        f.Engine.Enqueue(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Speech));
        Assert.Equal(0, receiver.Calls);
        Assert.Equal(0, external.Calls);
        f.Advance(50);
        Assert.Equal(1, receiver.Calls);
        Assert.Equal(0, external.Calls);
        Assert.True(f.Engine.NeedsFastCycle);
        f.Advance(50);
        Assert.Equal(1, receiver.Calls);
        Assert.Equal(1, external.Calls);
        Assert.False(f.Engine.NeedsFastCycle);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false, "move")]
    [InlineData(false, "remove")]
    [InlineData(false, "save")]
    [InlineData(true, "move")]
    [InlineData(true, "remove")]
    [InlineData(true, "save")]
    public void QueuedSignalCancelsOnlyTheChangedRecipientStack(bool bothMatch, string mutation)
    {
        var f = new Fixture();
        var antenna = f.Furni("antenna", x: 2);
        var other = f.Furni("antenna", x: 3);
        f.Trigger();
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [antenna.Id] }, x: 5);
        var valid = f.Action(x: 5);
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [bothMatch ? antenna.Id : other.Id] }, x: 7);
        var changed = f.Action(x: 7);
        var companion = f.Action(x: 7);
        var parent = SignalParent(f);
        Assert.True(f.Engine.Enqueue(new(WiredEventKind.Enter)));
        Assert.True(f.Engine.SendSignal(parent, [antenna], new()));

        if (mutation == "move") {
            changed.Item.GetX = 8;
        }
        else if (mutation == "remove") {
            Assert.True(f.Engine.Remove(changed.Item.Id));
            f.Furniture.Remove(changed.Item);
        }
        else {
            Assert.True(f.Engine.PublishConfigured(changed, changed.Configuration with { Text = "saved" }, () => { }));
        }

        f.Advance(50);
        Assert.Equal(1, valid.Calls);
        Assert.Equal(0, changed.Calls);
        Assert.Equal(0, companion.Calls);
        Assert.False(f.Engine.NeedsFastCycle);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedSignalRejectsItsAddressedAntennaLifetimeButNotAnotherBoxOnTheAntennaTile(bool removeAntenna)
    {
        var f = new Fixture();
        var antenna = f.Furni("antenna", x: 2);
        var unrelated = f.Action(x: 2);
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [antenna.Id] }, x: 5);
        var receiver = f.Action(x: 5);
        Assert.True(f.Engine.SendSignal(SignalParent(f), [antenna], new()));

        if (removeAntenna) {
            f.Furniture.Remove(antenna);
        }
        else {
            Assert.True(f.Engine.PublishConfigured(unrelated, unrelated.Configuration with { Text = "saved" }, () => { }));
        }

        f.Advance(50);
        Assert.Equal(removeAntenna ? 0 : 1, receiver.Calls);
        Assert.False(f.Engine.NeedsFastCycle);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
        Assert.Empty(f.Errors);
    }

    private static WiredRuntimeContext SignalParent(Fixture f) => new(f.Room, new(WiredEventKind.Enter),
        new(() => f.Furniture, () => f.Users, id => f.Furniture.FirstOrDefault(item => item.Id == id),
            id => f.Users.FirstOrDefault(user => user.VirtualId == id)), f);

    [Fact]
    public void SignalRejectsOrdinaryPickedFurnitureAsOfficialSaveRequiresAntennas()
    {
        var f = new Fixture();
        var receiverItem = f.Furni(x: 2);
        f.Trigger();
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [receiverItem.Id] }, x: 5);
        var receiver = f.Action(x: 5);
        f.Action(ctx => { Assert.False(f.Engine.SendSignal(ctx, [receiverItem], new())); return true; });
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        f.Advance(50);
        Assert.Equal(0, receiver.Calls);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CallsRunCalleeSelectionAndPolicyButBypassCalleeConditions(bool negativeCaller, bool calleeCondition)
    {
        var f = new Fixture();
        var user = f.User(1);
        var selected = f.Furni();
        f.Trigger(WiredEventKind.Speech);
        f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => !negativeCaller });
        var condition = f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => calleeCondition }, x: 2);
        var selector = f.Add(new Selector { SelectBody = _ => new(new([selected.Id]), WiredSelectionKind.Furni) }, x: 2);
        var addon = f.Add(new Addon { ApplyBody = context => { Assert.Contains(selected.Id, context.Selected.FurniIds); return true; } }, x: 2);
        var immediate = f.Action(context => { Assert.Contains(selected.Id, context.Selected.FurniIds); return true; }, x: 2);
        var delayed = f.Action(delay: 1, x: 2);
        var caller = f.Action(ctx => f.Engine.CallStacks(ctx, [immediate.Item], negativeCaller), negative: negativeCaller);
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Speech) { Actor = user, Message = "pulse" });
        Assert.Equal(1, caller.Calls);
        Assert.Equal(0, condition.Calls);
        Assert.Equal(1, immediate.Calls);
        Assert.Equal(0, delayed.Calls);
        f.Advance(499);
        Assert.Equal(0, delayed.Calls);
        f.Advance(1);
        Assert.Equal(1, delayed.Calls);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalledStackStillHonorsItsAdmissionAddon(bool negativeCaller)
    {
        var f = new Fixture();
        f.Trigger(WiredEventKind.Speech);
        var condition = f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => false }, x: 2);
        var attempts = 0;
        f.Add(new Addon { ApplyBody = _ => ++attempts == 1 }, x: 2);
        var receiver = f.Action(x: 2);
        f.Action(ctx => f.Engine.CallStacks(ctx, [receiver.Item], negativeCaller));
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Speech));
        Assert.Equal(1, receiver.Calls);
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Speech));
        Assert.Equal(1, receiver.Calls);
        Assert.Equal(2, attempts);
        Assert.Equal(0, condition.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void TimedTriggersEvaluateBeforeQueuedExternalEvents()
    {
        var f = new Fixture();
        var trace = new List<string>();
        f.Trigger();
        f.Add(new Timer(), x: 2);
        f.Add(new Addon { ApplyBody = _ => { trace.Add("event"); return true; } });
        f.Add(new Addon { ApplyBody = _ => { trace.Add("timer"); return true; } }, x: 2);
        f.Action();
        f.Action(x: 2);
        f.Engine.Enqueue(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        Assert.Equal(new[] { "timer", "event" }, trace);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void SignalDoesNotUseChannelAsFallbackForEmptyAntennaPicks()
    {
        var f = new Fixture();
        var antenna = f.Furni("antenna", x: 2);
        f.Trigger();
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [(int)antenna.Id, 100] }, x: 2);
        var receiver = f.Action(x: 2);
        f.Action(ctx => f.Engine.SendSignal(ctx, [antenna], new()));
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        Assert.Equal(0, receiver.Calls);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(WiredEventKind.BotReachedUser)]
    [InlineData(WiredEventKind.BotReachedFurni)]
    public void BotArrivalSeedsReachedTargetWithoutSelectingBot(WiredEventKind kind)
    {
        var f = new Fixture();
        var bot = f.User(1);
        bot.BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        var reached = f.User(2);
        var chair = f.Furni();
        f.Trigger(kind);
        var seen = new List<int[]>();
        f.Add(new Selector { SelectBody = ctx => { seen.Add(ctx.Triggering.UserIds.ToArray()); return new(new(), WiredSelectionKind.Furni); } });
        var action = f.Action();
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(kind)
        {
            Actor = bot,
            TargetUser = kind == WiredEventKind.BotReachedUser ? reached : null,
            EventItem = kind == WiredEventKind.BotReachedFurni ? chair : null
        }));
        Assert.Equal(kind == WiredEventKind.BotReachedUser ? new[] { reached.VirtualId } : [], Assert.Single(seen));
        f.Engine.OnFastCycle();
        Assert.Equal(1, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(WiredVariableTarget.User)]
    [InlineData(WiredVariableTarget.Furni)]
    public void VariableChangeAddsItsHolderAfterSelectorsAndConditions(WiredVariableTarget target)
    {
        var f = new Fixture();
        var holder = f.User(2);
        var furni = f.Furni();
        Assert.True(WiredBoxRegistry.TryGet("wf_trg_var_changed", out var descriptor));
        var trigger = new WiredVariableChangedTrigger(f.Room, f.Furni("wf_trg_var_changed"), descriptor);
        Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [(int)target, 1, 1, 1, 1, 1, 1, -1], Text = "custom:42" }, out var normalized, out var error), error);
        trigger.ApplyConfiguration(normalized);
        f.Engine.Add(trigger);
        f.Add(new Selector { SelectBody = ctx => { Assert.Empty(ctx.Triggering.UserIds); Assert.Empty(ctx.Triggering.FurniIds); return new(new(), WiredSelectionKind.Both); } });
        f.Add(new Box(WiredBoxCategory.Condition) { Body = ctx => { Assert.Empty(ctx.Selected.UserIds); Assert.Empty(ctx.Selected.FurniIds); return true; } });
        var action = f.Action(ctx =>
        {
            Assert.Equal(target == WiredVariableTarget.User ? new[] { holder.VirtualId } : [], ctx.Selected.UserIds);
            Assert.Equal(target == WiredVariableTarget.Furni ? new[] { furni.Id } : [], ctx.Selected.FurniIds);

            return true;
        });
        var entityId = target == WiredVariableTarget.User ? holder.VirtualId : (int)furni.Id;
        var change = new WiredVariableChange(f.Room.Id, new(42, target, 0), WiredVariableChangeKind.Updated,
            new(1, null, null), new(2, null, null), entityId, 0);
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Variable)
        {
            VariableChange = change,
            TargetUser = target == WiredVariableTarget.User ? holder : null,
            EventItem = target == WiredVariableTarget.Furni ? furni : null
        }));
        f.Engine.OnFastCycle();
        Assert.Equal(1, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void BlankBotNameMatchesAnyBotInModernArrivalTrigger()
    {
        var f = new Fixture();
        var bot = f.User(1);
        bot.BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        bot.BotData.Name = "Bob";
        bot.BotData.AiType = BotAiType.Generic;
        var reached = f.User(2);
        ModernTrigger(f, "wf_trg_bot_reached_avtr", new() { IntParams = [100], Text = "" });
        var action = f.Action();
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.BotReachedUser) { Actor = bot, TargetUser = reached }));
        f.Engine.OnFastCycle();
        Assert.Equal(1, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void DeferredSelectorTriggerChecksConditionsBeforeRejectingEvent()
    {
        var f = new Fixture();
        var actor = f.User(1);
        var eventItem = f.Furni();
        ModernTrigger(f, "wf_trg_click_furni", new() { IntParams = [200] });
        f.Add(new Selector { SelectBody = _ => new(new(), WiredSelectionKind.Furni) });
        var condition = f.Add(new Box(WiredBoxCategory.Condition));
        var action = f.Action();
        Assert.False(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.ClickFurni) { Actor = actor, EventItem = eventItem }));
        Assert.Equal(1, condition.Calls);
        Assert.Equal(0, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void ScoreTriggerAddsCurrentTeamMembersAfterConditions()
    {
        var f = new Fixture();
        var red = f.User(1);
        red.Team = Team.Red;
        var blue = f.User(2);
        blue.Team = Team.Blue;
        ModernTrigger(f, "wf_trg_score_achieved", new() { IntParams = [10, (int)Team.Red] });
        f.Add(new Box(WiredBoxCategory.Condition) { Body = ctx => { Assert.Empty(ctx.Selected.UserIds); return true; } });
        var action = f.Action(ctx => { Assert.Equal(new[] { red.VirtualId }, ctx.Selected.UserIds); return true; });
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Score) { Team = (int)Team.Red, PreviousValue = 9, Value = 10 }));
        f.Engine.OnFastCycle();
        Assert.Equal(1, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void LateRepeaterWaitsFullIntervalFromActualFiring()
    {
        var timers = new WiredTimedTriggers();
        var configuration = new WiredConfiguration { IntParams = [1] };
        Assert.False(timers.TryFire("wf_trg_periodically", 1, configuration, 0, 0, 0));
        Assert.True(timers.TryFire("wf_trg_periodically", 1, configuration, 750, 750, 0));
        Assert.False(timers.TryFire("wf_trg_periodically", 1, configuration, 1000, 1000, 0));
        Assert.True(timers.TryFire("wf_trg_periodically", 1, configuration, 1250, 1250, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectorQuantityFiltersKeepFirstEntriesAndLeaveOtherSourcesComplete(bool hasSelector)
    {
        var f = new Fixture();
        var items = new[] { f.Furni(), f.Furni(), f.Furni() };
        var users = new[] { f.User(1), f.User(2), f.User(3) };
        f.Trigger();

        if (hasSelector) {
            f.Add(new Selector { SelectBody = _ => new(new(items.Reverse().Select(item => item.Id), users.Reverse().Select(user => user.VirtualId)), WiredSelectionKind.Both) });
        }

        f.Add(new Addon { ApplyBody = ctx => { ctx.Policy.Addons.FurniLimit = 1; ctx.Policy.Addons.UserLimit = 1; return true; } });
        var action = f.Action(ctx =>
        {
            Assert.Equal(items, ctx.Targets.ResolveFurni(ctx, items.Select(item => item.Id), WiredSources.Selected));
            Assert.Equal(items[0], Assert.Single(ctx.Targets.ResolveFurni(ctx, [], WiredSources.Trigger)));
            Assert.Equal(users[0], Assert.Single(ctx.Targets.ResolveUsers(ctx, [], WiredSources.Trigger)));
            Assert.Equal(users, ctx.Targets.ResolveUsers(ctx, [], WiredSources.AllRoom));
            Assert.Equal(items[0].Id, Assert.Single(ctx.Triggering.FurniIds));
            Assert.Equal(hasSelector ? new[] { items[2].Id } : new[] { items[0].Id }, ctx.Selected.FurniIds);
            Assert.Equal(hasSelector ? new[] { users[2].VirtualId } : new[] { users[0].VirtualId }, ctx.Selected.UserIds);
            Assert.Equal(hasSelector ? new[] { items[2].Id } : [], ctx.SelectorPool.FurniIds);

            return true;
        });
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter) { Actor = users[0], EventItem = items[0] }));
        f.Engine.OnFastCycle();
        Assert.Equal(1, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void ExecuteInOrderRunsEqualDelayEffectsTogetherInStackOrder()
    {
        var f = new Fixture();
        f.Trigger();
        f.Add(new Addon { ApplyBody = ctx => { ctx.Policy.Addons.ExecuteInOrder = true; return true; } });
        var trace = new List<(string, long)>();
        f.Action(_ => { trace.Add(("A500", f.Now)); return true; }, delay: 1);
        f.Action(_ => { trace.Add(("B500", f.Now)); return true; }, delay: 1);
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        Assert.Empty(trace);
        f.Advance(499);
        Assert.Empty(trace);
        f.Advance(1);
        Assert.Equal(new[] { ("A500", 550L), ("B500", 550L) }, trace);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectsUseIndependentDelaysAsObservedOnOfficialHabbo(bool executeInOrder)
    {
        var f = new Fixture();
        f.Trigger();

        if (executeInOrder) {
            f.Add(new Addon { ApplyBody = ctx => { ctx.Policy.Addons.ExecuteInOrder = true; return true; } });
        }

        var trace = new List<(string, long)>();
        f.Action(_ => { trace.Add(("A500", f.Now)); return true; }, delay: 1);
        f.Action(_ => { trace.Add(("B0", f.Now)); return true; });
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        Assert.Equal(new[] { ("B0", 50L) }, trace);
        f.Advance(499);
        Assert.Single(trace);
        f.Advance(1);
        Assert.Equal(new[] { ("B0", 50L), ("A500", 550L) }, trace);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false, false, "B,A")]
    [InlineData(false, true, "A,B")]
    [InlineData(true, false, "A,B")]
    [InlineData(true, true, "A,B")]
    public void EqualDeadlinePermutationIsPerFiringAndExecuteInOrderKeepsHeightOrder(bool ordered, bool keepPermutation, string expected)
    {
        var f = new Fixture(new() { MaxExecutionsPerPass = 1 }, new FixedEffectOrderRandom(keepPermutation));
        f.Trigger();

        if (ordered) {
            f.Add(new Addon { ApplyBody = ctx => { ctx.Policy.Addons.ExecuteInOrder = true; return true; } });
        }

        var trace = new List<string>();
        f.Action(_ => { trace.Add("A"); return true; }, delay: 1);
        f.Action(_ => { trace.Add("B"); return true; }, delay: 1);
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        Assert.True(f.Engine.NeedsFastCycle);
        f.Advance(50); // A remaining condition/addon gate may use this budget pass.
        f.Advance(50);
        f.Advance(500);
        f.Advance(50); // Finish the second effect after a budget yield, retaining its sampled rank.
        Assert.Equal(expected.Split(','), trace);
        Assert.Empty(f.Errors);
    }

    private sealed class FixedEffectOrderRandom(bool keep) : Random
    {
        public override int Next(int minValue, int maxValue) => keep ? maxValue - 1 : minValue;
        public override int Next(int maxValue) => Next(0, maxValue);
    }

    [Fact]
    public void ExecutionBudgetDoesNotRestartAnIndependentEffectDelay()
    {
        var f = new Fixture(new() { MaxExecutionsPerPass = 1 });
        f.Trigger();
        var immediate = f.Action();
        var delayed = f.Action(delay: 1);
        f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter));
        f.Advance(50);
        Assert.Equal(1, immediate.Calls);
        f.Advance(50); // The delayed entry gets its first scheduler visit in a later budget pass.
        f.Advance(449);
        Assert.Equal(0, delayed.Calls);
        f.Advance(1);
        Assert.Equal(1, delayed.Calls); // Its deadline is still first resume at 50 + 500.
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void QueuedEventEvaluatesAtFirstBoundaryAndArmsEffectDelayAtSecond()
    {
        var f = new Fixture();
        var trigger = f.Trigger();
        var immediate = f.Action();
        var delayed = f.Action(delay: 1);
        f.Now = 5;
        Assert.True(f.Engine.Enqueue(new(WiredEventKind.Enter)));
        Assert.Equal(0, trigger.Calls);
        f.Advance(45);
        Assert.Equal(1, trigger.Calls);
        Assert.Equal(0, immediate.Calls);
        f.Advance(50);
        Assert.Equal(1, immediate.Calls);
        Assert.Equal(0, delayed.Calls);
        f.Advance(499);
        Assert.Equal(0, delayed.Calls);
        f.Advance(1);
        Assert.Equal(1, delayed.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void CalledStackSkipsConditionsAndExecutesZeroEffectsInTheCallingDrain()
    {
        var f = new Fixture();
        f.Trigger();
        f.Add(new Addon { ApplyBody = ctx => { ctx.Policy.Addons.ExecuteInOrder = true; return true; } });
        var target = f.Trigger(WiredEventKind.Signal, x: 2);
        var trace = new List<string>();
        f.Action(ctx => { ctx.Values["value"] = 1; trace.Add("before"); return f.Engine.CallStacks(ctx, [target.Item]); });
        f.Action(ctx => { ctx.Values["value"] = 2; trace.Add("after"); return true; });
        var condition = f.Add(new Box(WiredBoxCategory.Condition) { Body = _ => false }, x: 2);
        f.Action(_ => { trace.Add("callee"); return true; }, x: 2);
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Empty(trace);
        f.Advance(50);
        Assert.Equal(new[] { "before", "after", "callee" }, trace);
        Assert.Equal(0, condition.Calls);
        f.Advance(50);
        Assert.Equal(new[] { "before", "after", "callee" }, trace);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void SelectorQuantityFilterKeepsSurvivingEntryAheadOfReaddedEntry()
    {
        var f = new Fixture();
        var first = f.Furni();
        var second = f.Furni();
        f.Trigger();
        f.Add(new Selector { SelectBody = _ => new(new([first.Id, second.Id]), WiredSelectionKind.Furni) });
        f.Add(new Selector { SelectBody = _ => new(new([first.Id]), WiredSelectionKind.Furni, FiltersExisting: true, Invert: true) });
        f.Add(new Selector { SelectBody = _ => new(new([first.Id]), WiredSelectionKind.Furni) });
        f.Add(new Addon { ApplyBody = ctx => { ctx.Policy.Addons.FurniLimit = 1; return true; } });
        f.Action(ctx => { Assert.Equal(new[] { second.Id }, ctx.SelectorPool.FurniIds); return true; });
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter)));
        f.Engine.OnFastCycle();
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void DelayedAllRoomSourceIncludesNewArrivalsAndKeepsCapturedIdentityGuards()
    {
        var f = new Fixture();
        var originalUser = f.User(1);
        var originalItem = f.Furni();
        f.Trigger();
        Item? newItem = null;
        Plus.HabboHotel.Rooms.RoomUser? newUser = null;
        var action = f.Action(ctx =>
        {
            Assert.Contains(newItem!, ctx.Targets.ResolveFurni(ctx, [], WiredSources.AllRoom));
            Assert.Contains(newUser!, ctx.Targets.ResolveUsers(ctx, [], WiredSources.AllRoom));
            Assert.DoesNotContain(ctx.Targets.ResolveUsers(ctx, [], WiredSources.AllRoom), user => user.VirtualId == originalUser.VirtualId);
            Assert.DoesNotContain(ctx.Targets.ResolveFurni(ctx, [], WiredSources.AllRoom), item => item.Id == originalItem.Id);

            return true;
        }, delay: 1);
        Assert.True(f.Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter)));
        f.Engine.OnFastCycle();
        newItem = f.Furni();
        newUser = f.User(2);
        f.Users.Remove(originalUser);
        f.User(originalUser.VirtualId);
        f.Furniture.Remove(originalItem);
        f.Furniture.Add(new Item { Id = originalItem.Id, Definition = originalItem.Definition });
        f.Advance(500);
        Assert.Equal(1, action.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void DormantRoomPausesModernTimersButStillProcessesQueuedEvents()
    {
        var f = new Fixture();
        var occupant = f.User(1);
        var timer = RealTimer(f, "wf_trg_periodically", 1);
        var periodic = f.Action();
        f.Trigger(x: 3);
        var queued = f.Action(x: 3);
        f.Engine.OnFastCycle();
        f.Users.Remove(occupant);
        f.Advance(5000);
        f.Advance(50);
        Assert.Equal(0, periodic.Calls);
        Assert.True(f.Engine.Enqueue(new(WiredEventKind.Enter)));
        f.Advance(50);
        f.Advance(50);
        Assert.Equal(1, queued.Calls);
        Assert.Equal(0, periodic.Calls);
        f.User(2);
        f.Advance(50);
        Assert.Equal(0, periodic.Calls);
        f.Advance(50);
        Assert.Equal(1, periodic.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void MixedLegacyRepeaterUsesModernBoundaryAndDefaultIndependentDelays()
    {
        var f = new Fixture();
        var repeater = new LegacyRepeater { Item = f.Furni(), Instance = f.Room };
        repeater.Item.Definition.InteractionType = InteractionType.WiredTrigger;
        f.Engine.Add(repeater);
        var first = f.Action(delay: 1);
        var second = f.Action();
        Assert.True(f.Engine.RunPeriodicStack(repeater, []));
        Assert.Equal(0, second.Calls);
        f.Engine.OnFastCycle();
        Assert.Equal(1, second.Calls);
        f.Advance(499);
        Assert.Equal(0, first.Calls);
        Assert.Equal(1, second.Calls);
        f.Advance(1);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void DefaultSignalDepthStopsAfterTwentyNestedDeliveries()
    {
        var f = new Fixture();
        var antenna = f.Furni("antenna", x: 2);
        f.Trigger();
        ModernTrigger(f, "wf_trg_recv_signal", new() { IntParams = [0, 100], SelectedItems = [antenna.Id] }, x: 2);
        var receiver = f.Action(ctx => f.Engine.SendSignal(ctx, [antenna], new()), x: 2);
        f.Action(ctx => f.Engine.SendSignal(ctx, [antenna], new()));
        Assert.True(f.Engine.Enqueue(new(WiredEventKind.Enter)));

        for (var i = 0; i < 40; i++) {
            f.Advance(50);
        }

        Assert.Equal(20, receiver.Calls);
        Assert.False(f.Engine.NeedsFastCycle);
        Assert.Empty(f.Errors);
    }

    private static WiredModernTrigger ModernTrigger(Fixture f, string name, WiredConfiguration configuration, int x = 0)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        var item = f.Furni(name, x);
        var trigger = new WiredModernTrigger(f.Room, item, descriptor);
        Assert.True(trigger.TryValidateConfiguration(configuration, out var normalized, out var error), error);
        trigger.ApplyConfiguration(normalized);
        f.Engine.Add(trigger);

        return trigger;
    }
}
