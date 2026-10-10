using System.Diagnostics.CodeAnalysis;
using Dapper;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Plus.Core;
using Plus.Core.Settings;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.Database;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Commands;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent : IWiredRuntimeOperations
{
    private readonly Room _room;
    private readonly WiredStackEngine _engine;
    private readonly WiredTargetResolver _targets;
    private readonly ILogger _logger;
    private readonly TimeProvider _clock;
    private readonly IBotManagementStore _botStore;
    private readonly IGameClientManager _clients;
    private readonly IGroupManager _groups;
    private readonly IItemDataManager _definitions;
    private readonly ICommandManager _commands;
    private readonly IAccessControl _access;
    private readonly IItemTravelStore _travelStore;
    private readonly Plus.HabboHotel.Quests.IQuestManager? _quests;
    private readonly Action<WiredModernAction> _bindMovementPublication;

    public WiredComponent(Room instance, ILogger logger, TimeProvider clock, ISettingsManager settings, IWiredRoomSettingsFactory settingsFactory,
        IWiredConfigurationStore configurationStore, IDatabase database, IWiredRewardService rewardService,
        IBotManagementStore botStore, IGameClientManager clients, IGroupManager groups, IItemDataManager definitions,
        ICommandManager commands, IAccessControl access, IItemTravelStore travelStore, Plus.HabboHotel.Quests.IQuestManager? quests = null) //, RoomItem Items)
    {
        _room = instance;
        _counters = new(observeTransition: transition => _highscores?.Observe(transition), gameTransition: ControlGameTimer,
            canBegin: (item, origin, actor) => _highscores?.CanBeginOwned(item, origin, actor) ?? true);
        _logger = logger;
        _clock = clock;
        _configurationStore = configurationStore;
        _database = database;
        _rewards = rewardService;
        _botStore = botStore;
        _clients = clients;
        _groups = groups;
        _definitions = definitions;
        _commands = commands;
        _access = access;
        _travelStore = travelStore;
        _quests = quests;
        Settings = settingsFactory.Create(instance);
        _engine = new(
            () => (long)Stopwatch.GetElapsedTime(0).TotalMilliseconds,
            box => ReferenceEquals(_room.GetRoomItemHandler().GetItem(box.Item.Id), box.Item),
            IsActorPresent,
            OnEvent, ExceptionLogger.LogWiredException,
            WiredEngineLimits.FromSettings(settings.TryGetValue),
            CaptureActorVisit);
        _targets = new(
            () => _room.GetRoomItemHandler().GetFloor,
            () => _room.GetRoomUserManager().GetUserList(),
            id => _room.GetRoomItemHandler().GetItem(id),
            id => _room.GetRoomUserManager().GetRoomUserByVirtualId(id),
            () => _room.GetRoomItemHandler().GetWallAndFloor);
        _engine.BindRuntime(_room, _targets, this,
            () => _counters.HasRunning || WiredBotTargets.For(_room).HasTargets || _chests?.HasPending == true, PollCounters, FlushExternalChanges);
        _engine.ObserveEvent = (evt, now) =>
        {
            _selectorState.Observe(evt, now);

            if (evt.Kind == WiredEventKind.Leave && evt.Actor != null) {
                ForgetFxActor(evt.Actor);
                WiredAvatarState.For(_room).Forget(evt.Actor);
                WiredBotTargets.For(_room).Forget(evt.Actor);
                WiredGameState.For(_room).Forget(evt.Actor);

                if (_variables?.IsValueCreated == true) {
                    _variables.Value.HolderLeft(WiredVariableRuntimeFrames.UserHolder(evt.Actor));
                }
            }
        };
        // Only these concrete adapters are known silent for publication purposes.
        _engine.PublicationBridgesAreTrusted = true;
        _engine.PublicationEvaluationIsSilent = box => box is Plus.HabboHotel.Items.Wired.Modern.Addons.WiredAddonBox or Plus.HabboHotel.Items.Wired.Modern.Selectors.WiredSelectorBox
            or Plus.HabboHotel.Items.Wired.Modern.Conditions.WiredModernCondition
            || box.GetType() == typeof(Plus.HabboHotel.Items.Wired.Modern.Triggers.WiredModernTrigger)
            || box is Plus.HabboHotel.Items.Wired.Modern.Triggers.WiredModernTimedTrigger;
        _engine.PublicationObserverIsSilent = evt => evt.Kind != WiredEventKind.Leave;
        // This concrete flush seals from each drained snapshot before publishing its work.
        _engine.PublicationFlushIsSilent = () => true;
        _engine.PublicationPollIsSilent = () => _counterItems.Count == 0 && _chests == null
            && !WiredBotTargets.For(_room).HasTargets;
        _engine.LimitReached = NoteLimit;
        _engine.SpeechHidden = evt => evt.Actor?.GetClient()?.Send(
            new Plus.Communication.Packets.Outgoing.Rooms.Chat.WhisperComposer(evt.Actor.VirtualId, evt.Message, 0, evt.ChatStyle));
        _engine.CaptureSpeech = (context, trigger) => _variables?.IsValueCreated == true
            ? _variables.Value.CaptureSpeech(context, trigger) : null;
        _engine.ConfigurationPublished = box =>
        {
            UpdateClickEnvironment(box.Item.Id, box);

            if (_variables?.IsValueCreated == true) {
                _variables.Value.ConfigurationSaved(box);
            }
        };
        _bindMovementPublication = _engine.CreateMovementPublicationFactory(this, DispatchWalkTransition);
    }

    private int _highscoreClockAdmission;

    private void ControlGameTimer(WiredClockTransition transition)
    {
        var item = transition.Item;
        var start = transition.Reason is WiredClockReason.Start or WiredClockReason.Resume;
        var type = item.Definition.InteractionType;
        var banzai = type == InteractionType.Banzaicounter || item.Definition.ItemName == "bb_counter";
        var freeze = type == InteractionType.Freezetimer || item.Definition.ItemName == "es_counter";

        _highscoreClockAdmission++;

        try {
            if (start) {
                _room.GetGameManager().Reset();
                _highscores?.Start(transition);

                if (banzai) {
                    _room.GetBanzai().BanzaiStart();
                }
                else {
                    if (freeze) {
                        _room.GetFreeze().StartGame();
                    }
                    else {
                        _room.GetSoccer().StartGame();
                    }

                    TriggerEvent(WiredBoxType.TriggerGameStarts, null);
                }
            }
            else if (banzai) {
                _room.GetBanzai().BanzaiEnd();
            }
            else if (freeze) {
                _room.GetFreeze().StopGame();
            }
            else {
                _room.GetSoccer().StopGame();
            }
        }
        finally {
            _highscoreClockAdmission--;
        }
    }

    public void OnCycle()
    {
        _highscores?.OnOwnedPass();
        PublishStateWrites();
        _engine.OnCycle();
        FlushVariableFx();
    }

    internal void OnFastCycle()
    {
        _highscores?.OnOwnedPass();
        PublishStateWrites();
        _engine.OnFastCycle();

        if (_variables?.IsValueCreated == true && _variables.Value.FxDirty) {
            FlushVariableFx();
        }
    }
    internal bool NeedsFastCycle => _engine.NeedsFastCycle;
    internal void ObserveFastWork(Action<bool>? observer) => _engine.ObserveFastWork(observer);
    public bool Dispatch(WiredRuntimeEvent @event) => WithHighscoreSpeechOwner(@event.Kind == WiredEventKind.Speech, () => _engine.Mutate(() =>
    {
        if (_highscoreClockAdmission == 0 && @event.Kind is WiredEventKind.GameStart or WiredEventKind.GameEnd) {
            _highscores?.Invalidate();
        }

        if (@event.Kind == WiredEventKind.GameStart) {
            WiredGameState.For(_room).ResetQuotas();
        }
        else if (@event.Kind == WiredEventKind.GameEnd) {
            _counters.OnGameEnded();
        }

        return @event.Kind == WiredEventKind.Speech ? _engine.DispatchSynchronously(@event) : _engine.Enqueue(@event);
    }));
    public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) =>
        _engine.CallStacks(context, targets, negative);
    public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) =>
        _engine.SendSignal(context, receivers, selection, negative);
    public bool ScheduleAux(WiredRuntimeContext context, int delayMilliseconds, Action callback, Action? onCancelled = null) =>
        _engine.ScheduleAux(context, delayMilliseconds, callback, onCancelled);
    public void DispatchWalkTransition(RoomUser actor, IEnumerable<Item> before, IEnumerable<Item> after)
    {
        var oldItems = before.DistinctBy(x => x.Id).ToDictionary(x => x.Id);
        var newItems = after.DistinctBy(x => x.Id).ToDictionary(x => x.Id);

        foreach (var item in oldItems.Values.Where(x => !newItems.ContainsKey(x.Id)).OrderBy(x => x.GetZ).ThenBy(x => x.Id)) {
            if (ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item)) {
                item.UserWalksOffFurni(actor);
            }
        }

        foreach (var item in newItems.Values.Where(x => !oldItems.ContainsKey(x.Id)).OrderBy(x => x.GetZ).ThenBy(x => x.Id)) {
            if (ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item)) {
                item.UserWalksOnFurni(actor);
            }
        }
    }
    public void ResetTimers(IEnumerable<Item> targets) => _engine.ResetTimers(targets);
    public bool PublishConfigured(IWiredConfiguredItem original, WiredConfiguration validated, Action persistValidated) =>
        _engine.PublishConfigured(original, validated, persistValidated);

    internal bool IsActorPresent(object[] arguments) => arguments.Length == 0 || arguments[0] is not Habbo player
        || player.InRoom && ReferenceEquals(player.CurrentRoom, _room);

    internal object? CaptureActorVisit(object[] arguments) => arguments.Length > 0 && arguments[0] is Habbo player
        ? _room.GetRoomUserManager()?.GetRoomUserByHabbo(player.Id) : null;

    public bool RunStack(IWiredItem source, params object[] arguments) => _engine.RunStack(source, arguments);

    internal bool RunStack(IWiredItem source, object[] arguments, Action onAccepted) =>
        _engine.RunStack(source, arguments, onAccepted);

    internal bool RunPeriodicStack(IWiredItem source, object[][] actors) => _engine.RunPeriodicStack(source, actors);

    internal bool CallStacks(IEnumerable<Item> targets, params object[] arguments) => _engine.CallStacks(targets, arguments);

    public IWiredItem? LoadWiredBox(Item item)
    {
        if (DescriptorOf(item) is not { } descriptor || CreateConfiguredBox(item, descriptor) is not { } configured) {
            _logger.LogWarning("Unsupported wired type {WiredType} on item {ItemId} in room {RoomId}",
                item.Definition.WiredType, item.Id, _room.Id);

            return null;
        }

        try {
            var saved = ConfigurationStore.Load(item.Id, descriptor);

            // A card without a saved row keeps its compiled defaults and gains persistence only on a successful save.
            if (saved != null) {
                if (saved.SelectedItems.Concat(saved.SecondarySelectedItems)
                    .Any(id => _room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true)) {
                    throw new InvalidDataException("Saved static selections cannot reference temporary room furniture.");
                }

                if (!configured.TryValidateConfiguration(saved, out var validated, out var error)) {
                    throw new InvalidDataException(error);
                }

                configured.ApplyConfiguration(validated);
            }

            if (_variables?.IsValueCreated == true) {
                _variables.Value.ConfigurationLoaded(configured);
            }

            return AddBox(configured) ? configured : null;
        }
        catch (Exception error) {
            _logger.LogError(error, "Cannot load Wired configuration for item {ItemId} in room {RoomId}; saved bytes retained", item.Id, _room.Id);

            return null;
        }
    }

    /// <summary>The registry descriptor of placed furniture: named by its definition, or by its legacy wired type.</summary>
    internal static WiredBoxDescriptor? DescriptorOf(Item item) => item.Definition.WiredDescriptor
        ?? (WiredBoxRegistry.TryGetByLegacyType(item.Definition.WiredType, out var descriptor) ? descriptor : null);

    public bool IsTrigger(Item item) => item.Definition.InteractionType == InteractionType.WiredTrigger;

    public bool IsEffect(Item item) => item.Definition.InteractionType == InteractionType.WiredEffect;

    public bool IsCondition(Item item) => item.Definition.InteractionType == InteractionType.WiredCondition;

    public bool OtherBoxHasItem(IWiredItem box, uint itemId)
    {
        if (box == null) {
            return false;
        }

        ICollection<IWiredItem> items = GetEffects(box).Where(x => x.Item.Id != box.Item.Id).ToList();

        if (items != null && items.Count > 0) {
            foreach (var item in items) {
                if (item.Type != WiredBoxType.EffectMoveAndRotate && item.Type != WiredBoxType.EffectMoveFurniFromNearestUser && item.Type != WiredBoxType.EffectMoveFurniToNearestUser) {
                    continue;
                }

                if (item.SetItems == null || item.SetItems.Count == 0) {
                    continue;
                }

                if (item.SetItems.ContainsKey(itemId)) {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TriggerEvent(WiredBoxType type, params object[] arguments) =>
        WithHighscoreSpeechOwner(type == WiredBoxType.TriggerUserSays, () => TriggerEventCore(type, arguments));

    private bool TriggerEventCore(WiredBoxType type, object[] arguments)
    {
        arguments ??= [];

        WiredEventKind? kind = type switch
        {
            WiredBoxType.TriggerRoomEnter => WiredEventKind.Enter,
            WiredBoxType.TriggerUserSays => WiredEventKind.Speech,
            WiredBoxType.TriggerWalkOnFurni => WiredEventKind.WalkOn,
            WiredBoxType.TriggerWalkOffFurni => WiredEventKind.WalkOff,
            WiredBoxType.TriggerStateChanges => WiredEventKind.Use,
            WiredBoxType.TriggerGameStarts => WiredEventKind.GameStart,
            WiredBoxType.TriggerGameEnds => WiredEventKind.GameEnd,
            WiredBoxType.TriggerUserFurniCollision => WiredEventKind.Collision,
            _ => null
        };
        var actor = arguments.OfType<RoomUser>().FirstOrDefault()
            ?? (arguments.OfType<Habbo>().FirstOrDefault() is { } habbo
                ? _room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id) : null);
        var typed = kind == null ? null : new WiredRuntimeEvent(kind.Value)
        {
            Actor = actor,
            EventItem = arguments.OfType<Item>().FirstOrDefault(),
            Message = kind == WiredEventKind.Speech ? arguments.OfType<string>().FirstOrDefault() ?? "" : "",
            ChatStyle = kind == WiredEventKind.Speech ? arguments.OfType<int>().FirstOrDefault() : 0,
            ChatType = kind == WiredEventKind.Speech && arguments.OfType<bool>().FirstOrDefault() ? 1 : 0
        };

        if (kind is WiredEventKind.GameStart or WiredEventKind.GameEnd) {
            return _engine.Mutate(() =>
            {
                if (_highscoreClockAdmission == 0) {
                    _highscores?.Invalidate();
                }

                if (kind == WiredEventKind.GameStart) {
                    WiredGameState.For(_room).ResetQuotas();
                }
                else {
                    _counters.OnGameEnded();
                }

                return _engine.DispatchLegacy(type, typed, arguments);
            });
        }

        return _engine.DispatchLegacy(type, typed, arguments);
    }

    public ICollection<IWiredItem> GetTriggers(IWiredItem item) => _engine.GetBoxes(item, InteractionType.WiredTrigger);

    public ICollection<IWiredItem> GetEffects(IWiredItem item) => _engine.GetBoxes(item, InteractionType.WiredEffect);

    public IWiredItem? GetRandomEffect(ICollection<IWiredItem> effects)
    {
        return effects.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
    }

    public bool OnUserFurniCollision(Room room, Item item)
    {
        if (room == null || item == null) {
            return false;
        }

        foreach (var point in item.GetSides()) {
            if (room.GetGameMap().SquareHasUsers(point.X, point.Y)) {
                var users = room.GetGameMap().GetRoomUsers(point);

                if (users != null && users.Count > 0) {
                    foreach (var user in users.ToList()) {
                        if (user == null) {
                            continue;
                        }

                        item.UserFurniCollision(user);
                    }
                }
                else {
                    continue;
                }
            }
            else {
                continue;
            }
        }

        return true;
    }

    public ICollection<IWiredItem> GetConditions(IWiredItem item) => _engine.GetBoxes(item, InteractionType.WiredCondition);

    public void OnEvent(Item item)
    {
        // Hidden wired boxes are not on the clients, so they do not flash.
        if (_room.HideWired) {
            return;
        }

        // A box saved mid-flash loads as "1" with no reset pending, so only a pending reset means it is still lit.
        if (item.LegacyDataString == "1" && item.UpdateNeeded) {
            return;
        }

        item.LegacyDataString = "1";
        item.UpdateState(false, true);
        item.RequestUpdate(2, true);
    }

    public bool AddBox(IWiredItem item) => _engine.Mutate(() =>
    {
        if (!_engine.Add(item)) {
            return false;
        }

        UpdateClickEnvironment(item.Item.Id, item);

        return true;
    });

    public bool TryRemove(uint itemId) => _engine.Mutate(() =>
    {
        if (!_engine.Remove(itemId)) {
            return false;
        }

        UpdateClickEnvironment(itemId);

        return true;
    });

    public bool TryGet(uint id, [NotNullWhen(true)] out IWiredItem? item) => _engine.TryGet(id, out item);

    public void Cleanup()
    {
        WiredTemporaryEffects.For(_room).Clear();
        _engine.Clear();
        _selectorState.Reset();
        WiredBotTargets.For(_room).Clear();
        WiredProjectileFlights.For(_room).Clear();
        WiredGameState.For(_room).Clear();
        WiredAvatarState.For(_room).Clear();
        _counters.Clear();
        _counterItems.Clear();
        _clickUserTriggers.Clear();
        _fxViewers.Clear();
        _roomLog.Clear();
        ClearStateWrites();
    }
}
