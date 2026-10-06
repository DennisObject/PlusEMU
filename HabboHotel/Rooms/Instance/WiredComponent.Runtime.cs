using Plus.HabboHotel.Items;
using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.Database;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent
{
    private readonly WiredSelectorRoomState _selectorState = new();
    private readonly WiredCounterController _counters = new();
    private readonly WiredRoomLog _roomLog = new();
    // The monitor polls several times a second; the full log stays on the paged log request.
    private const int MonitorHistory = 100;
    private readonly Dictionary<uint, Item> _counterItems = [];
    private readonly IWiredConfigurationStore _configurationStore;
    private readonly IDatabase _database;
    private readonly IWiredRewardService _rewards;
    private Lazy<WiredRoomVariables>? _variables;
    public WiredRoomSettings Settings
    {
        get;
    }
    internal DateTimeOffset CalendarTime =>
        TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Settings.ExplicitTimeZone ?? _clock.LocalTimeZone);
    private IWiredConfigurationStore ConfigurationStore => _configurationStore;
    public WiredRoomVariables Variables => (_variables ??= new(() => new(_room,
        _database, _clock, builtinRead: ReadBuiltin, builtinWrite: WriteBuiltin, stateChanged: PublishBuiltinStateChanged)
    {
        TimeZone = () => Settings.ExplicitTimeZone ?? TimeZoneInfo.Utc
    })).Value;

    // Returns a detached concrete candidate. Registration and persistence belong to the loader/publisher.
    public IWiredConfiguredItem? CreateConfiguredBox(Item item, WiredBoxDescriptor? descriptor = null)
    {
        // Plus command boxes retain CommandManager dispatch and its feedback; speech is a different behavior.
        if (item.Definition.WiredType == WiredBoxType.TriggerUserSaysCommand)
        {
            return null;
        }

        descriptor ??= item.Definition.WiredDescriptor;

        if (descriptor == null)
        {
            return null;
        }

        IWiredConfiguredItem? box = null;
        WiredConfiguration? defaults = null;

        if (descriptor.Category == WiredBoxCategory.Selector && WiredSelectorModule.Names.Contains(descriptor.CanonicalName))
        {
            box = new WiredSelectorBox(_room, item, descriptor, _selectorState, _groups,
                context => WiredSelectorVariableBridge.Create(context, Variables.Module));
        }
        else if (descriptor.Category == WiredBoxCategory.Addon && WiredAddonModule.Names.Contains(descriptor.CanonicalName))
        {
            box = new WiredAddonBox(_room, item, descriptor, _selectorState, _groups,
                context => WiredSelectorVariableBridge.Create(context, Variables.Module));
        }
        else if (descriptor.Category == WiredBoxCategory.Trigger && WiredTriggerConfiguration.Events.ContainsKey(descriptor.CanonicalName))
        {
            box = WiredTriggerConfiguration.IsTimed(descriptor.CanonicalName)
                ? new WiredModernTimedTrigger(_room, item, descriptor) : new WiredModernTrigger(_room, item, descriptor);
            defaults = WiredTriggerConfiguration.Defaults(descriptor.CanonicalName);
        }
        else if (descriptor.Category == WiredBoxCategory.Condition && WiredConditionConfiguration.Supports(descriptor.CanonicalName))
        {
            // Calendar predicates use the configured zone; elapsed durations use the shared UTC room clock.
            box = new WiredModernCondition(_room, item, descriptor, _groups, ReadCounterMilliseconds,
                descriptor.CanonicalName is "wf_cnd_match_time" or "wf_cnd_match_date" or "wf_cnd_date_rng_active"
                    ? () => CalendarTime : () => _clock.GetUtcNow());
            defaults = WiredConditionConfiguration.Defaults(descriptor.CanonicalName,
                descriptor.CanonicalName == "wf_cnd_match_date" ? CalendarTime.Year : 0);
        }
        else if (descriptor.Category == WiredBoxCategory.Action && WiredModernAction.Supports(descriptor.CanonicalName))
        {
            box = new WiredModernAction(_room, item, descriptor, _counters, @event => Dispatch(@event),
                DispatchWalkTransition, _roomLog, _logger, _clock, _rewards, _botStore, _clients, _definitions, _travelStore);
            defaults = WiredActionConfiguration.Defaults(descriptor.CanonicalName);
        }
        else if (WiredVariableExecutors.Supports(descriptor.CanonicalName) || WiredVariableMetadataBox.Supports(descriptor.CanonicalName)
            || WiredVariableAddonBox.Supports(descriptor.CanonicalName) || descriptor.CanonicalName is "wf_xtra_text_input_variable" or "wf_trg_var_changed"
            || descriptor.Category == WiredBoxCategory.Variable)
        {
            box = Variables.CreateBox(item);
        }

        if (box != null && defaults != null)
        {
            if (!box.TryValidateConfiguration(defaults, out var validated, out var error))
            {
                throw new InvalidDataException(error);
            }

            box.ApplyConfiguration(validated);
        }

        return box;
    }

    public void BeforeActorLeaves(RoomUser actor) => _engine.Mutate(() =>
    {
        WiredTemporaryEffects.For(_room).Forget(actor);
        _engine.ActorLeaving(actor);
        WiredAvatarState.For(_room).Thaw(actor);
        WiredAvatarState.For(_room).Forget(actor);
        WiredBotTargets.For(_room).Forget(actor);
        WiredGameState.For(_room).Forget(actor);

        return true;
    });

    public WiredRoomLogPage ReadLogs(int page, int amount, int level = -1, string query = "", int source = -1) =>
        _roomLog.Read(page, amount, level, query, source);

    public WiredMonitorSnapshot ReadMonitor() => new(_engine.ReadStats(), _engine.Limits.MaxExecutionsPerPass,
        _engine.Limits.MaxPendingStacks, _engine.Limits.MaxDepth, _roomLog.Summarize(MonitorHistory));

    public void ClearLogs() => _roomLog.Clear();

    private void NoteLimit(WiredEngineLimit limit, string reason) => _roomLog.Append(WiredRoomLog.ErrorLevel, limit switch
    {
        WiredEngineLimit.ExecutionBudget => WiredLogSource.ExecutionCap,
        WiredEngineLimit.PendingStacks => WiredLogSource.DelayedEventsCap,
        _ => WiredLogSource.RecursionTimeout
    }, 0, "", reason, _clock.GetUtcNow());

    public void AttachRoomItem(Item item) => _engine.Mutate(() =>
    {
        if (WiredCounterController.Recognizes(item) && ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item)
            && (!_counterItems.TryGetValue(item.Id, out var previous) || !ReferenceEquals(previous, item)))
        {
            _counterItems[item.Id] = item;
            _counters.Attach(item);
        }

        if (item.IsTemporary && ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item)
            && !_engine.TryGet(item.Id, out _))
        {
            var box = CreateConfiguredBox(item) ?? GenerateNewBox(item);

            if (box != null)
            {
                AddBox(box);
            }
        }

        return true;
    });

    public void DetachRoomItem(Item item) => _engine.Mutate(() =>
    {
        ForgetFxItem(item);
        _counters.Forget(item);
        WiredProjectileFlights.For(_room).Forget(item);

        if (_counterItems.TryGetValue(item.Id, out var attached) && ReferenceEquals(attached, item))
        {
            _counterItems.Remove(item.Id);
        }

        if (_variables?.IsValueCreated == true)
        {
            _variables.Value.ItemDetached(item);
        }

        _engine.Remove(item.Id);

        return true;
    });

    /// <summary>
    /// Boxes leaving the room for good (pickup, eject, room deletion) are placed again with default settings. Their saved
    /// settings are dropped first, so if that fails nothing changes and the boxes stay placed with their settings. Done in an
    /// engine pass: a save already on its way then finds the box detached and does not write the settings back.
    /// </summary>
    public void ResetRoomItems(IEnumerable<Item> items) => _engine.Mutate(() =>
    {
        var boxes = items.Where(item => item.IsWired && !item.IsTemporary).ToList();

        if (boxes.Count == 0)
        {
            return false;
        }

        ConfigurationStore.Reset(boxes.Select(item => item.Id).ToArray());

        foreach (var box in boxes)
        {
            DetachRoomItem(box);
        }

        return true;
    });

    public bool TryUseCounter(Item item, int parameter) => _engine.Mutate(() =>
    {
        if (!WiredCounterController.Recognizes(item) || !ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item))
        {
            return false;
        }

        AttachRoomItem(item);
        var mark = FurnitureStateEvents.Mark();
        var changed = _counters.Use(item, parameter, _engine.NowMilliseconds);
        PublishCounterChanges(_counters.TakeChanges(), mark);

        return changed;
    });

    private long? ReadCounterMilliseconds(Item item)
    {
        AttachRoomItem(item);

        return ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item) ? _counters.ReadMilliseconds(item) : null;
    }

    private void PollCounters(long now)
    {
        foreach (var stale in _counterItems.Values.Where(item => !ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item)).ToArray())
        {
            DetachRoomItem(stale);
        }

        var mark = FurnitureStateEvents.Mark();
        PublishCounterChanges(_counters.Poll(now), mark);

        if (WiredBotTargets.For(_room).HasTargets)
        {
            foreach (var arrival in WiredBotTargets.For(_room).Poll(_room))
            {
                QueueRuntimeEvent(arrival);
            }
        }

        FlushExternalChanges();
    }

    private void FlushExternalChanges()
    {
        // Display writes made by boxes are already queued; they are reported by the room's pass.
        PublishCounterChanges(_counters.TakeChanges(), FurnitureStateEvents.Mark());

        if (_variables?.IsValueCreated != true)
        {
            return;
        }

        foreach (var change in _variables.Value.DrainChanges())
        {
            QueueRuntimeEvent(new(WiredEventKind.Variable)
            {
                Code = unchecked((int)change.Key.DefinitionId),
                Action = (int)change.Kind,
                VariableChange = change,
                PreviousValue = change.Before?.Value ?? 0,
                Value = change.After?.Value ?? 0,
                EventItem = change.Key.Target == WiredVariableTarget.Furni ? _room.GetRoomItemHandler().GetItem((uint)change.EntityId) : null,
                TargetUser = change.Key.Target == WiredVariableTarget.User ? _room.GetRoomUserManager().GetRoomUserByVirtualId(change.EntityId) : null
            }, change.Depth + 1);
        }
    }

    private void QueueRuntimeEvent(WiredRuntimeEvent @event, int? depth = null)
    {
        if (!_engine.Enqueue(@event, depth))
        {
            _logger.LogWarning("Wired {EventKind} event rejected by room queue/depth limits in room {RoomId}", @event.Kind, _room.Id);
        }
    }

    // A display change is reported only for a display write this thread made after the mark.
    private void PublishCounterChanges(IEnumerable<WiredCounterChange> changes, long mark)
    {
        foreach (var change in changes)
        {
            if (!ReferenceEquals(_room.GetRoomItemHandler().GetItem(change.Item.Id), change.Item))
            {
                continue;
            }

            if (change.Event.Kind == WiredEventKind.GameStart)
            {
                _room.GetGameManager().Reset();
                WiredGameState.For(_room).ResetQuotas();
            }

            if (change.DisplayChanged)
            {
                change.Item.UpdateState();
            }

            if (change.Event.Kind == WiredEventKind.StateChanged
                && (!change.DisplayChanged || !FurnitureStateEvents.TakeWriteSince(change.Item, mark)))
            {
                continue;
            }

            QueueRuntimeEvent(change.Event);
        }
    }
}

public sealed record WiredMonitorSnapshot(WiredEngineWindow Engine, int ExecutionsPerPass, int PendingLimit, int DepthLimit, WiredRoomLogSummary Logs);
