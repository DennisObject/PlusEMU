using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Revisions;
using Plus.Communication.Packets.Outgoing;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

public class WiredStackEngineTests
{
    [Fact]
    public void ConditionsGateOnceAndRandomAddonChoosesOneActionWithActor()
    {
        var fixture = new Fixture();
        var actor = new object();
        var trigger = fixture.Trigger();
        var condition = fixture.Condition(_ => true);
        var addon = fixture.Add(new Box(InteractionType.WiredEffect, WiredBoxType.AddonRandomEffect));
        var effects = new[] { fixture.Effect(), fixture.Effect(), fixture.Effect() };

        Assert.True(fixture.Engine.RunStack(trigger, [actor]));

        Assert.Single(condition.Calls);
        Assert.Empty(addon.Calls);
        var call = Assert.Single(effects.SelectMany(x => x.Calls));
        Assert.Same(actor, Assert.Single(call));
        Assert.Contains(addon.Item.Id, fixture.Flashes);
    }

    [Fact]
    public void FailedConditionBlocksChatAcceptanceAndAllActions()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        fixture.Condition(_ => false);
        var effect = fixture.Effect();
        var accepted = false;

        Assert.False(fixture.Engine.RunStack(trigger, [new object()], () => accepted = true));
        Assert.False(accepted);
        Assert.Empty(effect.Calls);
    }

    [Fact]
    public void ChatAcceptanceIsSynchronousEvenWithDelayedOrFailedActions()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var delayed = fixture.Effect(delay: 2, execute: _ => false);
        var later = fixture.Effect();
        var accepted = false;
        trigger.Body = args => fixture.Engine.RunStack(trigger, args, () => accepted = true);

        Assert.True(fixture.Engine.Dispatch(trigger.Type, new object()));
        Assert.True(accepted);
        Assert.Empty(delayed.Calls);
        Assert.Single(later.Calls);
        fixture.Advance(1000);
        Assert.Single(delayed.Calls);
        Assert.Single(later.Calls);
    }

    [Fact]
    public void DispatchAggregatesSuccessAndVisitsSameEventTriggersOnce()
    {
        var fixture = new Fixture();
        var first = fixture.Trigger();
        first.Body = _ => true;
        var second = fixture.Trigger();
        second.Body = _ => false;
        var third = fixture.Trigger();
        third.Body = _ => throw new InvalidOperationException("bad trigger");
        var fourth = fixture.Trigger();

        Assert.True(fixture.Engine.Dispatch(first.Type, new object()));
        Assert.All(new[] { first, second, third, fourth }, trigger => Assert.Single(trigger.Calls));
        Assert.Single(fixture.Errors);
    }

    [Fact]
    public void IndependentDelaysUseHalfSecondsAndStableFiringOrder()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var observed = new List<(string, object)>();
        // Add out of order to prove Z sorting, rather than insertion order.
        var second = fixture.Effect(delay: 2, execute: args => { observed.Add(("second", args[0])); return true; });
        second.Item.GetZ = 3;
        var first = fixture.Effect(delay: 1, execute: args => { observed.Add(("first", args[0])); return true; });
        first.Item.GetZ = 1;
        var alice = new object();
        var bob = new object();
        fixture.Engine.RunStack(trigger, [alice]);
        fixture.Engine.RunStack(trigger, [bob]);

        fixture.Advance(499);
        Assert.Empty(observed);
        fixture.Advance(1);
        Assert.Equal(new[] { ("first", alice), ("first", bob) }, observed);
        fixture.Advance(499);
        Assert.Equal(2, observed.Count);
        fixture.Advance(1);
        Assert.Equal(new[] { ("first", alice), ("first", bob), ("second", alice), ("second", bob) }, observed);
        Assert.Equal(0, first.Cycles);
        Assert.Equal(0, second.Cycles);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    public void EqualDelaysShareADeadlineAndLateTicksDoNotAddAnotherWait(int elapsed)
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var first = fixture.Effect(delay: 1);
        var second = fixture.Effect(delay: 1);
        fixture.Engine.RunStack(trigger, []);

        fixture.Advance(499);
        Assert.Empty(first.Calls);
        Assert.Empty(second.Calls);
        fixture.Advance(elapsed - 499);
        Assert.Single(first.Calls);
        Assert.Single(second.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImmediateActionRunsAtFiringOnEitherSideOfDelayedAction(bool above)
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var delayed = fixture.Effect(delay: 1);
        var immediate = fixture.Effect();
        immediate.Item.GetZ = above ? 10 : 0;

        fixture.Engine.RunStack(trigger, []);
        Assert.Single(immediate.Calls);
        Assert.Empty(delayed.Calls);
        fixture.Advance(500);
        Assert.Single(delayed.Calls);
    }

    [Fact]
    public void RepeatedFiringsAndArgumentsAreIndependent()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 1);
        var alice = new object();
        var bob = new object();
        object[] arguments = [alice];
        fixture.Engine.RunStack(trigger, arguments);
        arguments[0] = bob;
        fixture.Engine.RunStack(trigger, arguments);

        fixture.Advance(500);
        Assert.Equal(2, effect.Calls.Count);
        Assert.Same(alice, effect.Calls[0][0]);
        Assert.Same(bob, effect.Calls[1][0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PendingActorCannotExecuteAfterLeavingOrChangingRoom(bool changeRoom)
    {
        var room = EmptyRoom();
        var wired = new WiredComponent(room);
        var fixture = new Fixture(actorPresent: wired.IsActorPresent);
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 1);
        var player = new Habbo { CurrentRoom = room };
        Assert.True(fixture.Engine.RunStack(trigger, [player]));

        player.CurrentRoom = changeRoom ? EmptyRoom() : null;
        fixture.Advance(500);
        Assert.Empty(effect.Calls);
        Assert.False(fixture.Engine.RunStack(trigger, [player]));
    }

    [Fact]
    public void RemovedBoxesDoNotDispatchOrTickAndPendingChainIsCancelled()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var first = fixture.Effect(delay: 1);
        var second = fixture.Effect(delay: 2);
        fixture.Engine.RunStack(trigger, []);
        fixture.Detached.Add(first.Item.Id);
        fixture.Detached.Add(trigger.Item.Id);

        fixture.Advance(500);
        Assert.Empty(first.Calls);
        Assert.Empty(second.Calls);
        Assert.False(fixture.Engine.Dispatch(trigger.Type));
        Assert.Equal(0, first.Cycles);
        Assert.False(fixture.Engine.TryGet(trigger.Item.Id, out _));
    }

    [Fact]
    public void PeriodicTriggersKeepTheirCountdownAndDetachedOnesNeverCycle()
    {
        var fixture = new Fixture();
        var periodic = fixture.Add(new PeriodicBox { Delay = 2, TickCount = 2 });
        fixture.Advance(500);
        fixture.Advance(500);
        Assert.Equal(0, periodic.Cycles);
        fixture.Advance(500);
        Assert.Equal(1, periodic.Cycles);
        fixture.Detached.Add(periodic.Item.Id);
        periodic.TickCount = 0;
        fixture.Advance(500);
        Assert.Equal(1, periodic.Cycles);
    }

    [Fact]
    public void RemovingAnActionCancelsItsCapturedStackAndReaddingDoesNotReviveIt()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var first = fixture.Effect(delay: 1);
        var second = fixture.Effect(delay: 2);
        fixture.Engine.RunStack(trigger, []);
        Assert.True(fixture.Engine.Remove(first.Item.Id));
        fixture.Engine.Add(first);

        fixture.Advance(500);
        Assert.Empty(first.Calls);
        Assert.Empty(second.Calls);
    }

    [Fact]
    public void ReconfiguringAnyCapturedBoxCancelsTheOldFiring()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var condition = fixture.Condition(_ => true);
        var effect = fixture.Effect(delay: 1);
        fixture.Engine.RunStack(trigger, []);
        fixture.Engine.CancelPending(condition);
        fixture.Advance(500);
        Assert.Empty(effect.Calls);
        fixture.Engine.RunStack(trigger, []);
        fixture.Advance(500);
        Assert.Single(effect.Calls);
    }

    [Fact]
    public void CoordinateChangesRefreshStackAndMovedPendingActionIsSkipped()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 1);
        fixture.Engine.RunStack(trigger, []);
        effect.Item.GetX = 7;

        fixture.Advance(500);
        Assert.Empty(effect.Calls);
        Assert.Empty(fixture.Engine.GetBoxes(trigger, InteractionType.WiredEffect));
        trigger.Item.GetX = 7;
        Assert.Single(fixture.Engine.GetBoxes(trigger, InteractionType.WiredEffect));
        fixture.Engine.RunStack(trigger, []);
        fixture.Advance(500);
        Assert.Single(effect.Calls);
    }

    [Fact]
    public void FalseAndThrowingActionsDoNotBlockLaterActions()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var first = fixture.Effect(execute: _ => false);
        var second = fixture.Effect(execute: _ => throw new InvalidOperationException("bad effect"));
        var third = fixture.Effect();

        Assert.True(fixture.Engine.RunStack(trigger, []));
        Assert.All(new[] { first, second, third }, effect => Assert.Single(effect.Calls));
        Assert.Single(fixture.Errors);
    }

    [Fact]
    public void PeriodicConditionsKeepAnyActorPerConditionAndActionsHaveNoActor()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var alice = new object();
        var bob = new object();
        fixture.Condition(args => ReferenceEquals(args[0], alice));
        fixture.Condition(args => ReferenceEquals(args[0], bob));
        var effect = fixture.Effect();

        Assert.True(fixture.Engine.RunPeriodicStack(trigger, [[alice], [bob]]));
        Assert.Empty(Assert.Single(effect.Calls));
        Assert.False(fixture.Engine.RunPeriodicStack(trigger, [[alice]]));
        Assert.False(fixture.Engine.RunPeriodicStack(trigger, []));
        Assert.False(fixture.Engine.RunStack(trigger, [alice]));
        Assert.Single(effect.Calls);
    }

    [Fact]
    public void StackCallsUseConditionsRandomAndOneExecutionPerSelectedTile()
    {
        var fixture = new Fixture();
        var source = fixture.Trigger();
        Item[] targets = [];
        var caller = fixture.Effect(execute: args => fixture.Engine.CallStacks(targets, args));
        caller.Item.GetX = source.Item.GetX = 1;
        var target = fixture.Trigger();
        var condition = fixture.Condition(args => args.Length == 1);
        fixture.Add(new Box(InteractionType.WiredEffect, WiredBoxType.AddonRandomEffect));
        var first = fixture.Effect();
        var second = fixture.Effect();
        targets = [target.Item, first.Item, second.Item];
        var actor = new object();

        Assert.True(fixture.Engine.RunStack(source, [actor]));
        Assert.Single(condition.Calls);
        Assert.Same(actor, Assert.Single(first.Calls.Concat(second.Calls))[0]);

    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RecursiveStackCallsRetainDepthAcrossImmediateAndDelayedActions(int delay)
    {
        var fixture = new Fixture(limits: new() { MaxDepth = 3 });
        var target = fixture.Trigger();
        var caller = fixture.Effect(delay: delay);
        caller.Body = args => fixture.Engine.CallStacks([target.Item], args);
        fixture.Engine.RunStack(target, [new object()]);
        for (var i = 0; i < 10; i++) fixture.Advance(500);

        Assert.Equal(4, caller.Calls.Count); // root depth 0, calls 1..3; depth 4 rejected
        Assert.Empty(fixture.Errors);
    }

    [Fact]
    public void PerActionBudgetDefersRemainingActionsWithoutLoss()
    {
        var fixture = new Fixture(limits: new() { MaxExecutionsPerPass = 2 });
        var trigger = fixture.Trigger();
        var effects = Enumerable.Range(0, 5).Select(_ => fixture.Effect()).ToArray();
        fixture.Engine.RunStack(trigger, []);
        Assert.Equal(2, effects.Sum(x => x.Calls.Count));
        fixture.Engine.OnCycle();
        Assert.Equal(4, effects.Sum(x => x.Calls.Count));
        fixture.Engine.OnCycle();
        Assert.All(effects, effect => Assert.Single(effect.Calls));
    }

    [Fact]
    public void QueueLimitRejectsNewFiringsAndCleanupCancelsPendingWork()
    {
        var fixture = new Fixture(limits: new() { MaxPendingStacks = 1 });
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 1);
        Assert.True(fixture.Engine.RunStack(trigger, []));
        Assert.False(fixture.Engine.RunStack(trigger, []));
        fixture.Engine.Clear();
        fixture.Advance(500);
        Assert.Empty(effect.Calls);
    }

    [Fact]
    public void ConcurrentFiringsRetainEveryActorWithoutSharedSlots()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 1);
        var actors = Enumerable.Range(0, 100).Select(_ => new object()).ToArray();
        Parallel.ForEach(actors, actor => Assert.True(fixture.Engine.RunStack(trigger, [actor])));
        fixture.Advance(500);
        Assert.Equal(actors.Length, effect.Calls.Count);
        Assert.Equal(actors.ToHashSet(), effect.Calls.Select(args => args[0]).ToHashSet());
    }

    [Fact]
    public void ReconfigurationDuringAnActionCancelsItsRemainingChain()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        fixture.Effect(execute: _ => { fixture.Engine.CancelPending(trigger); return true; });
        var later = fixture.Effect(delay: 1);
        fixture.Engine.RunStack(trigger, []);
        fixture.Advance(500);
        Assert.Empty(later.Calls);
    }

    [Fact]
    public void PendingLimitAlsoCoversNestedChainsThatEnqueueBeforeTheirCaller()
    {
        var fixture = new Fixture(limits: new() { MaxPendingStacks = 1 });
        var root = fixture.Trigger();
        root.Item.GetX = 1;
        var target = fixture.Trigger();
        var targetEffect = fixture.Effect(delay: 1);
        var caller = fixture.Effect(execute: args => fixture.Engine.CallStacks([target.Item], args));
        caller.Item.GetX = 1;
        var rootEffect = fixture.Effect(delay: 1);
        rootEffect.Item.GetX = 1;

        fixture.Engine.RunStack(root, []);
        fixture.Advance(500);
        Assert.Single(rootEffect.Calls);
        Assert.Empty(targetEffect.Calls);
    }

    [Fact]
    public void SpendingTheBudgetExactlyIsNotACapButTurningWorkAwayIs()
    {
        var fixture = new Fixture(limits: new() { MaxExecutionsPerPass = 2 });
        var notes = new List<WiredEngineLimit>();
        fixture.Engine.LimitReached = (limit, _) => notes.Add(limit);
        var trigger = fixture.Trigger();
        var effects = new[] { fixture.Effect(), fixture.Effect() };
        Assert.True(fixture.Engine.RunStack(trigger, []));
        Assert.All(effects, effect => Assert.Single(effect.Calls));
        Assert.Empty(notes);

        var third = fixture.Effect();
        Assert.True(fixture.Engine.RunStack(trigger, []));
        Assert.Equal([WiredEngineLimit.ExecutionBudget], notes);
        fixture.Engine.OnCycle();
        Assert.Single(third.Calls); // the deferred action still ran: the note changed nothing
        Assert.Single(notes);
    }

    [Fact]
    public void DepthAndQueueRejectionsAreReportedAndStillRejected()
    {
        var deep = new Fixture(limits: new() { MaxDepth = 3 });
        var depthNotes = new List<WiredEngineLimit>();
        deep.Engine.LimitReached = (limit, _) => depthNotes.Add(limit);
        var target = deep.Trigger();
        var caller = deep.Effect();
        caller.Body = args => deep.Engine.CallStacks([target.Item], args);
        deep.Engine.RunStack(target, [new object()]);
        Assert.Equal(4, caller.Calls.Count);
        Assert.Equal([WiredEngineLimit.Depth], depthNotes);

        var full = new Fixture(limits: new() { MaxPendingStacks = 1 });
        var queueNotes = new List<string>();
        full.Engine.LimitReached = (limit, reason) => { Assert.Equal(WiredEngineLimit.PendingStacks, limit); queueNotes.Add(reason); };
        var trigger = full.Trigger();
        full.Effect(delay: 1);
        Assert.True(full.Engine.RunStack(trigger, []));
        Assert.Empty(queueNotes);
        Assert.False(full.Engine.RunStack(trigger, []));
        Assert.Equal("The wait queue was full (1 of 1 chains); new work was dropped.", Assert.Single(queueNotes));
    }

    [Fact]
    public void StatsReportTheLastFullWindowOfPassesThatRanBoxes()
    {
        var fixture = new Fixture(limits: new() { MaxExecutionsPerPass = 10 });
        var trigger = fixture.Trigger();
        fixture.Effect(); fixture.Effect(); fixture.Effect();
        fixture.Engine.RunStack(trigger, []);
        for (var i = 0; i < 20; i++) fixture.Engine.GetBoxes(trigger, InteractionType.WiredEffect); // inspection passes run nothing
        Assert.Equal(new WiredEngineWindow(1000, 0, 0, 0, 0, 0), fixture.Engine.ReadStats()); // the first window is still open

        fixture.Advance(1000);
        var window = fixture.Engine.ReadStats();
        Assert.Equal((1000, 3, 0, 0), (window.WindowMs, window.PeakExecutions, window.PeakDepth, window.Pending));
        Assert.True(window.AverageMs <= window.PeakMs);

        fixture.Advance(2500); // an idle gap leaves nothing to report
        Assert.Equal(0, fixture.Engine.ReadStats().PeakExecutions);
    }

    [Fact]
    public void PendingIsReadLiveAfterCancellationAndClearWithoutAnotherPass()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        fixture.Effect(delay: 1);
        Assert.True(fixture.Engine.RunStack(trigger, []));
        Assert.True(fixture.Engine.RunStack(trigger, []));
        Assert.Equal(2, fixture.Engine.ReadStats().Pending);
        fixture.Engine.CancelPending(trigger);
        Assert.Equal(0, fixture.Engine.ReadStats().Pending);
        Assert.True(fixture.Engine.RunStack(trigger, []));
        fixture.Engine.Clear();
        Assert.Equal(0, fixture.Engine.ReadStats().Pending);
    }

    [Fact]
    public void SafetyLimitsReadExistingSettingsAndDefaultWhenAbsent()
    {
        var defaults = WiredEngineLimits.FromSettings(_ => "0");
        var configured = WiredEngineLimits.FromSettings(key => key == "wired.max_depth" ? "8" : "200");
        Assert.Equal(32, defaults.MaxDepth);
        Assert.Equal(10000, defaults.MaxExecutionsPerPass);
        Assert.Equal(8, configured.MaxDepth);
        Assert.Equal(200, configured.MaxExecutionsPerPass);
        Assert.Equal(200, configured.MaxPendingStacks);
    }

    [Fact]
    public void EveryPreviouslyConstructibleBoxRetainsItsTypeAndConfigurationShape()
    {
        var wired = new WiredComponent(EmptyRoom());
        WiredBoxType[] unsupported = [WiredBoxType.None, WiredBoxType.EffectMoveFurniFromNearestUser,
            WiredBoxType.EffectBotCommunicatesToUserBox, WiredBoxType.ConditionFurniTypeMatches,
            WiredBoxType.ConditionFurniTypeDoesntMatch];
        var supported = Enum.GetValues<WiredBoxType>().Except(unsupported).ToArray();
        Assert.Equal(50, supported.Length);
        foreach (var type in supported)
        {
            var item = new Item { Definition = Definition(InteractionType.None, type) };
            var box = Assert.IsAssignableFrom<IWiredItem>(wired.GenerateNewBox(item));
            Assert.Equal(type, box.Type);
            Assert.Same(item, box.Item);
            Assert.Empty(box.SetItems);
            box.StringData = "configuration";
            box.ItemsData = "42:1,2,3,4,state;";
            box.BoolData = true;
            Assert.Equal("configuration", box.StringData);
            Assert.Equal("42:1,2,3,4,state;", box.ItemsData);
            Assert.True(box.BoolData);
            if (box is IWiredCycle cycle)
            {
                cycle.Delay = 7;
                Assert.Equal(7, cycle.Delay);
                if (type != WiredBoxType.TriggerRepeat) Assert.False(cycle.OnCycle());
            }
        }
        foreach (var type in unsupported)
            Assert.Null(wired.LoadWiredBox(new Item { Definition = Definition(InteractionType.None, type) }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KickWarningPrecedesGraceAndProtectedActorsAreNeverScheduled(bool protectedActor)
    {
        var actor = ActorRoom(protectedActor);
        var wired = new WiredComponent(actor.Room);
        var fixture = new Fixture(actorPresent: wired.IsActorPresent, actorVisit: wired.CaptureActorVisit);
        var trigger = fixture.Trigger();
        var kick = fixture.Add(new KickUserBox(actor.Room,
            new Item { Definition = Definition(InteractionType.WiredEffect, WiredBoxType.EffectKickUser) })
            { StringData = "You will be removed" });

        Assert.True(fixture.Engine.RunStack(trigger, [actor.Player]));
        Assert.Equal(new[] { ServerPacketHeader.WhisperComposer }, actor.Packets);
        Assert.Same(actor.Room, actor.Player.CurrentRoom);
        fixture.Advance(1499);
        Assert.Single(actor.Packets);
        fixture.Advance(1);
        Assert.Equal(1, actor.Packets.Count(id => id == ServerPacketHeader.WhisperComposer));
        Assert.Equal(protectedActor ? 0 : 1, actor.Packets.Count(id => id == ServerPacketHeader.CloseConnectionComposer));
        Assert.Empty(fixture.Errors);
    }

    [Fact]
    public void TeleportGlowBeginsWhenFiringIsAcceptedBeforeItsDelay()
    {
        var actor = ActorRoom();
        var wired = new WiredComponent(actor.Room);
        var fixture = new Fixture(actorPresent: wired.IsActorPresent, actorVisit: wired.CaptureActorVisit);
        var trigger = fixture.Trigger();
        var teleport = fixture.Add(new TeleportUserBox(actor.Room,
            new Item { Definition = Definition(InteractionType.WiredEffect, WiredBoxType.EffectTeleportToFurni) })
            { Delay = 2 });
        var target = new Item { Id = 100 };
        teleport.SetItems.TryAdd(target.Id, target);

        Assert.True(fixture.Engine.RunStack(trigger, [actor.Player]));
        Assert.Equal(4, actor.Player.Effects.CurrentEffect);
        Assert.Equal(new[] { ServerPacketHeader.AvatarEffectComposer }, actor.Packets);
        Assert.DoesNotContain(teleport.Item.Id, fixture.Flashes);
        fixture.Advance(999);
        Assert.DoesNotContain(teleport.Item.Id, fixture.Flashes);
        Assert.Equal(4, actor.Player.Effects.CurrentEffect);
        Assert.Empty(fixture.Errors);
    }

    [Fact]
    public void PreparationRunsOncePerAcceptedActorAndRejectionHasNoFeedback()
    {
        var fixture = new Fixture(limits: new() { MaxPendingStacks = 2 });
        var trigger = fixture.Trigger();
        var condition = fixture.Condition(_ => true);
        var prepared = fixture.Add(new PreparedBox { Delay = 1 });
        var alice = new object();
        var bob = new object();
        Assert.True(fixture.Engine.RunStack(trigger, [alice]));
        Assert.True(fixture.Engine.RunStack(trigger, [bob]));
        Assert.False(fixture.Engine.RunStack(trigger, [new object()]));
        Assert.Equal(new[] { alice, bob }, prepared.Preparations.Select(args => args[0]));
        Assert.Empty(prepared.Calls);
        fixture.Advance(500);
        Assert.Equal(new[] { alice, bob }, prepared.Calls.Select(args => args[0]));
        condition.Body = _ => false;
        Assert.False(fixture.Engine.RunStack(trigger, [new object()]));
        Assert.Equal(2, prepared.Preparations.Count);
    }

    [Fact]
    public void PreparationUsesTheExecutionBudgetAndRemainingActionsResume()
    {
        var fixture = new Fixture(limits: new() { MaxExecutionsPerPass = 2 });
        var trigger = fixture.Trigger();
        var prepared = fixture.Add(new PreparedBox { Delay = 1 });
        var first = fixture.Effect();
        var second = fixture.Effect();
        fixture.Engine.RunStack(trigger, []);
        Assert.Single(prepared.Preparations);
        Assert.Single(first.Calls);
        Assert.Empty(second.Calls);
        fixture.Advance(500);
        Assert.Single(second.Calls);
        Assert.Single(prepared.Calls);
    }

    [Fact]
    public void LeavingAndReenteringTheSameRoomCancelsThePreviousVisit()
    {
        var actor = ActorRoom();
        var wired = new WiredComponent(actor.Room);
        var fixture = new Fixture(actorPresent: wired.IsActorPresent, actorVisit: wired.CaptureActorVisit);
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 4);
        var previousVisit = wired.CaptureActorVisit([actor.Player]);
        fixture.Engine.RunStack(trigger, [actor.Player]);
        actor.Player.CurrentRoom = null;
        actor.Users.Clear();
        actor.Player.CurrentRoom = actor.Room;
        var newVisit = new RoomUser(actor.Player.Id, 0, 1, actor.Room);
        SetPrivate(newVisit, "_mClient", actor.Player.Client);
        actor.Users.TryAdd(1, newVisit);
        Assert.NotSame(previousVisit, wired.CaptureActorVisit([actor.Player]));

        fixture.Advance(2000);
        Assert.Empty(effect.Calls);
        Assert.True(fixture.Engine.RunStack(trigger, [actor.Player]));
        fixture.Advance(2000);
        Assert.Single(effect.Calls);
    }

    [Fact]
    public void MovingSourceAwayAndBackThroughSetStateCancelsItsOldFiring()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 4);
        fixture.Engine.RunStack(trigger, []);
        trigger.Item.SetState(7, 0, trigger.Item.GetZ, []);
        trigger.Item.SetState(0, 0, trigger.Item.GetZ, []);
        Assert.Equal(2, trigger.Item.MovementGeneration);

        fixture.Advance(2000);
        Assert.Empty(effect.Calls);
        fixture.Engine.RunStack(trigger, []);
        fixture.Advance(2000);
        Assert.Single(effect.Calls);
    }

    [Fact]
    public void SourceMovementReleasesPendingCapacityBeforeTheOldDeadline()
    {
        var fixture = new Fixture(limits: new() { MaxPendingStacks = 1 });
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 4);
        Assert.True(fixture.Engine.RunStack(trigger, ["old"]));
        trigger.Item.SetState(7, 0, trigger.Item.GetZ, []);
        trigger.Item.SetState(0, 0, trigger.Item.GetZ, []);
        Assert.True(fixture.Engine.RunStack(trigger, ["new"]));
        fixture.Advance(2000);
        Assert.Equal("new", Assert.Single(effect.Calls)[0]);
    }

    [Fact]
    public void SettingUnchangedSourceCoordinatesDoesNotCancelPendingWork()
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var effect = fixture.Effect(delay: 1);
        fixture.Engine.RunStack(trigger, []);
        trigger.Item.SetState(0, 0, trigger.Item.GetZ, []);
        trigger.Item.SetState(0, 0, double.PositiveInfinity, []);
        Assert.Equal(0, trigger.Item.MovementGeneration);
        fixture.Advance(500);
        Assert.Single(effect.Calls);
    }

    [Theory]
    [InlineData(1, 500)]
    [InlineData(2, 2000)]
    public void ActionsAcrossFiringsFollowGlobalDeadlineHeightAndItemOrder(int secondDelay, int elapsed)
    {
        var fixture = new Fixture();
        var trigger = fixture.Trigger();
        var observed = new List<string>();
        fixture.Effect(delay: 1, execute: args => { observed.Add($"first-{args[0]}"); return true; });
        fixture.Effect(delay: secondDelay, execute: args => { observed.Add($"second-{args[0]}"); return true; });
        fixture.Engine.RunStack(trigger, ["Alice"]);
        fixture.Engine.RunStack(trigger, ["Bob"]);
        fixture.Advance(elapsed);
        Assert.Equal(new[] { "first-Alice", "first-Bob", "second-Alice", "second-Bob" }, observed);
    }

    [Fact]
    public void EqualDeadlinesAcrossTilesUseItemIdBeforeFiringSequence()
    {
        var fixture = new Fixture();
        var observed = new List<string>();
        var lowTrigger = fixture.Trigger();
        var low = fixture.Effect(delay: 1, execute: _ => { observed.Add("low"); return true; });
        var highTrigger = fixture.Trigger();
        highTrigger.Item.GetX = 1;
        var high = fixture.Effect(delay: 1, execute: _ => { observed.Add("high"); return true; });
        high.Item.GetX = 1;
        low.Item.GetZ = high.Item.GetZ = 2;
        fixture.Engine.RunStack(highTrigger, []);
        fixture.Engine.RunStack(lowTrigger, []);
        fixture.Advance(500);
        Assert.Equal(new[] { "low", "high" }, observed);
    }

    [Fact]
    public void InsufficientPreparationBudgetRejectsBeforeChatAcceptance()
    {
        var fixture = new Fixture(limits: new() { MaxExecutionsPerPass = 1 });
        var trigger = fixture.Trigger();
        fixture.Condition(_ => true);
        var prepared = fixture.Add(new PreparedBox { Delay = 1 });
        var accepted = false;
        Assert.False(fixture.Engine.RunStack(trigger, [], () => accepted = true));
        Assert.False(accepted);
        Assert.Empty(prepared.Preparations);
        fixture.Advance(500);
        Assert.Empty(prepared.Calls);
    }

    [Fact]
    public void BudgetDeferralRetainsGlobalDeadlinesAndFiringOrder()
    {
        var fixture = new Fixture(limits: new() { MaxExecutionsPerPass = 1 });
        var trigger = fixture.Trigger();
        var observed = new List<string>();
        fixture.Effect(delay: 1, execute: args => { observed.Add($"first-{args[0]}"); return true; });
        fixture.Effect(delay: 2, execute: args => { observed.Add($"second-{args[0]}"); return true; });
        fixture.Engine.RunStack(trigger, ["Alice"]);
        fixture.Engine.RunStack(trigger, ["Bob"]);
        fixture.Advance(2000);
        for (var i = 0; i < 3; i++) fixture.Engine.OnCycle();
        Assert.Equal(new[] { "first-Alice", "first-Bob", "second-Alice", "second-Bob" }, observed);
    }

    private static void SetPrivate(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static (Room Room, Habbo Player, ConcurrentDictionary<int, RoomUser> Users, List<uint> Packets) ActorRoom(bool protectedActor = false)
    {
        var room = EmptyRoom();
        var manager = new RoomUserManager(room);
        SetPrivate(room, "_roomUserManager", manager);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var packets = new List<uint>();
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory())
        {
            Revision = new Revision { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
            {
                [ServerPacketHeader.WhisperComposer] = ServerPacketHeader.WhisperComposer,
                [ServerPacketHeader.CloseConnectionComposer] = ServerPacketHeader.CloseConnectionComposer,
                [ServerPacketHeader.AvatarEffectComposer] = ServerPacketHeader.AvatarEffectComposer,
                [ServerPacketHeader.UserRemoveComposer] = ServerPacketHeader.UserRemoveComposer
            } },
            SendCallback = args => { packets.Add(BinaryPrimitives.ReadUInt16BigEndian(args.MemoryBuffer.Span.Slice(4, 2))); return true; }
        };
        var player = new Habbo { Id = 1, Username = "actor", CurrentRoom = room, Client = client,
            Access = EditorTestSupport.Access(protectedActor ? [PermissionKeys.ModerationTool] : []), Effects = new EffectsComponent() };
        client.SetHabbo(player);
        SetPrivate(player.Effects, "_habbo", player);
        var visit = new RoomUser(player.Id, 0, 0, room);
        SetPrivate(visit, "_mClient", client);
        users.TryAdd(0, visit);
        return (room, player, users, packets);
    }

    private static Room EmptyRoom() => (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));

    private static ItemDefinition Definition(InteractionType kind, WiredBoxType type) => new()
    {
        InteractionType = kind, WiredType = type, ItemName = "", PublicName = "",
        VendingIds = [], AdjustableHeights = []
    };

    private sealed class Fixture
    {
        private uint _nextId;
        public long Now { get; private set; }
        public HashSet<uint> Detached { get; } = [];
        public List<uint> Flashes { get; } = [];
        public List<Exception> Errors { get; } = [];
        public WiredStackEngine Engine { get; }
        public Fixture(Func<object[], bool>? actorPresent = null, WiredEngineLimits? limits = null,
            Func<object[], object?>? actorVisit = null) =>
            Engine = new(() => Now, box => !Detached.Contains(box.Item.Id), actorPresent ?? (_ => true),
                item => Flashes.Add(item.Id), Errors.Add, limits, actorVisit);
        public T Add<T>(T box) where T : IWiredItem
        {
            box.Item.Id = ++_nextId;
            box.Item.GetZ = _nextId;
            Engine.Add(box);
            return box;
        }
        public Box Trigger() => Add(new Box(InteractionType.WiredTrigger, WiredBoxType.TriggerUserSays));
        public Box Condition(Func<object[], bool> execute) =>
            Add(new Box(InteractionType.WiredCondition, WiredBoxType.ConditionTriggererOnFurni) { Body = execute });
        public DelayedBox Effect(int delay = 0, Func<object[], bool>? execute = null) =>
            Add(new DelayedBox { Delay = delay, Body = execute ?? (_ => true) });
        public void Advance(long milliseconds) { Now += milliseconds; Engine.OnCycle(); }
    }

    private class Box(InteractionType kind, WiredBoxType type) : IWiredItem
    {
        public Room Instance { get; set; } = null!;
        public Item Item { get; set; } = new() { Definition = Definition(kind, type) };
        public WiredBoxType Type => type;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public List<object[]> Calls { get; } = [];
        public Func<object[], bool> Body { get; set; } = _ => true;
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments) { Calls.Add(arguments.ToArray()); return Body(arguments); }
    }

    private class DelayedBox() : Box(InteractionType.WiredEffect, WiredBoxType.EffectShowMessage), IWiredCycle
    {
        public int Delay { get; set; }
        public int TickCount { get; set; }
        public int Cycles { get; private set; }
        public bool OnCycle() { Cycles++; return true; }
    }

    private sealed class PreparedBox : DelayedBox, IWiredFiringPreparation
    {
        public List<object[]> Preparations { get; } = [];
        public bool Prepare(params object[] arguments) { Preparations.Add(arguments.ToArray()); return true; }
    }

    private sealed class PeriodicBox() : Box(InteractionType.WiredTrigger, WiredBoxType.TriggerRepeat), IWiredCycle
    {
        public int Delay { get; set; }
        public int TickCount { get; set; }
        public int Cycles { get; private set; }
        public bool OnCycle() { Cycles++; TickCount = Delay; return true; }
    }
}
