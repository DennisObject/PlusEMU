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
using Plus.HabboHotel.Items.Wired.Boxes;
using Plus.HabboHotel.Items.Wired.Boxes.Conditions;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Boxes.Triggers;
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
    private readonly Action<WiredModernAction> _bindMovementPublication;

    public WiredComponent(Room instance, ILogger logger, TimeProvider clock, ISettingsManager settings, IWiredRoomSettingsFactory settingsFactory,
        IWiredConfigurationStore configurationStore, IDatabase database, IWiredRewardService rewardService,
        IBotManagementStore botStore, IGameClientManager clients, IGroupManager groups, IItemDataManager definitions,
        ICommandManager commands, IAccessControl access, IItemTravelStore travelStore) //, RoomItem Items)
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
    public bool PublishPromotion(IWiredItem original, IWiredConfiguredItem candidate, WiredConfiguration validated, Action persistValidated) =>
        _engine.PublishPromotion(original, candidate, validated, persistValidated);
    public bool PublishLegacy(IWiredItem original, IWiredItem candidate, Action persistCandidate) =>
        _engine.PublishLegacy(original, candidate, persistCandidate);

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
        var newBox = GenerateNewBox(item);
        var descriptor = item.Definition.WiredDescriptor;

        if (descriptor == null && newBox != null && WiredLegacyEditorProjection.TryGetDescriptor(newBox, out var projected)) {
            descriptor = projected;
        }

        if (descriptor != null) {
            try {
                var saved = ConfigurationStore.Load(item.Id, descriptor);

                if (saved != null && saved.SelectedItems.Concat(saved.SecondarySelectedItems)
                    .Any(id => _room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true)) {
                    throw new InvalidDataException("Saved static selections cannot reference temporary room furniture.");
                }

                var selected = WiredBoxLoading.Select(newBox, CreateConfiguredBox(item, descriptor), saved);

                if (selected is IWiredConfiguredItem configured) {
                    if (_variables?.IsValueCreated == true) {
                        _variables.Value.ConfigurationLoaded(configured);
                    }

                    if (!AddBox(configured)) {
                        return null;
                    }

                    return configured;
                }

                newBox = selected;
            }
            catch (Exception error) {
                _logger.LogError(error, "Cannot load Wired configuration for item {ItemId} in room {RoomId}; saved bytes retained", item.Id, _room.Id);

                return null;
            }
        }

        if (newBox == null) {
            _logger.LogWarning("Unsupported wired type {WiredType} on item {ItemId} in room {RoomId}",
                item.Definition.WiredType, item.Id, _room.Id);

            return null;
        }

        using (var connection = _database.Connection()) {
            var row = connection.QuerySingleOrDefault<WiredItemRow>(
                "SELECT items,delay,`string` AS StringData,`bool` AS BoolData FROM wired_items WHERE id=@id LIMIT 1", new { item.Id });

            if (row != null) {
                if (string.IsNullOrEmpty(row.StringData)) {
                    if (newBox.Type == WiredBoxType.ConditionMatchStateAndPosition || newBox.Type == WiredBoxType.ConditionDontMatchStateAndPosition) {
                        newBox.StringData = "0;0;0";
                    }
                    else if (newBox.Type == WiredBoxType.ConditionUserCountInRoom || newBox.Type == WiredBoxType.ConditionUserCountDoesntInRoom) {
                        newBox.StringData = "0;0";
                    }
                    else if (newBox.Type == WiredBoxType.ConditionFurniHasNoFurni) {
                        newBox.StringData = "0";
                    }
                    else if (newBox.Type == WiredBoxType.EffectMatchPosition) {
                        newBox.StringData = "0;0;0";
                    }
                    else if (newBox.Type == WiredBoxType.EffectMoveAndRotate) {
                        newBox.StringData = "0;0";
                    }
                }

                newBox.StringData = row.StringData;
                newBox.BoolData = row.BoolData;
                newBox.ItemsData = row.Items;

                if (newBox is IWiredCycle) {
                    var box = (IWiredCycle)newBox;
                    box.Delay = row.Delay;
                }

                foreach (var str in row.Items.Split(';')) {
                    var id = 0;
                    var sId = "0";

                    if (str.Contains(':')) {
                        sId = str.Split(':')[0];
                    }

                    if (int.TryParse(str, out id) || int.TryParse(sId, out id)) {
                        var selectedItem = _room.GetRoomItemHandler().GetItem(Convert.ToUInt32(id));

                        if (selectedItem == null) {
                            continue;
                        }

                        newBox.SetItems.TryAdd(selectedItem.Id, selectedItem);
                    }
                }
            }
            else {
                newBox.ItemsData = "";
                newBox.StringData = "";
                newBox.BoolData = false;
                SaveBox(newBox);
            }
        }

        if (!AddBox(newBox)) {
            // ummm
        }

        return newBox;
    }

    public IWiredItem? GenerateNewBox(Item item)
    {
        switch (item.Definition.WiredType) {
            case WiredBoxType.TriggerRoomEnter:
                return new RoomEnterBox(_room, item);
            case WiredBoxType.TriggerRepeat:
                return new RepeaterBox(_room, item);
            case WiredBoxType.TriggerStateChanges:
                return new StateChangesBox(_room, item);
            case WiredBoxType.TriggerUserSays:
                return new UserSaysBox(_room, item);
            case WiredBoxType.TriggerWalkOffFurni:
                return new UserWalksOffBox(_room, item);
            case WiredBoxType.TriggerWalkOnFurni:
                return new UserWalksOnBox(_room, item);
            case WiredBoxType.TriggerGameStarts:
                return new GameStartsBox(_room, item);
            case WiredBoxType.TriggerGameEnds:
                return new GameEndsBox(_room, item);
            case WiredBoxType.TriggerUserFurniCollision:
                return new UserFurniCollision(_room, item);
            case WiredBoxType.TriggerUserSaysCommand:
                return new UserSaysCommandBox(_room, item, _commands);
            case WiredBoxType.EffectShowMessage:
                return new ShowMessageBox(_room, item, _clients);
            case WiredBoxType.EffectTeleportToFurni:
                return new TeleportUserBox(_room, item);
            case WiredBoxType.EffectToggleFurniState:
                return new ToggleFurniBox(_room, item);
            case WiredBoxType.EffectMoveAndRotate:
                return new MoveAndRotateBox(_room, item);
            case WiredBoxType.EffectKickUser:
                return new KickUserBox(_room, item);
            case WiredBoxType.EffectMuteTriggerer:
                return new MuteTriggererBox(_room, item, _clock);
            case WiredBoxType.EffectGiveReward:
                return new GiveRewardBox(_room, item);
            case WiredBoxType.EffectMatchPosition:
                return new MatchPositionBox(_room, item);
            case WiredBoxType.EffectAddActorToTeam:
                return new AddActorToTeamBox(_room, item);
            case WiredBoxType.EffectRemoveActorFromTeam:
                return new RemoveActorFromTeamBox(_room, item);
            /*

            case WiredBoxType.EffectMoveFurniToNearestUser:
                return new MoveFurniToNearestUserBox(_room, Item);
            case WiredBoxType.EffectMoveFurniFromNearestUser:
                return new MoveFurniFromNearestUserBox(_room, Item);

               */
            case WiredBoxType.ConditionFurniHasUsers:
                return new FurniHasUsersBox(_room, item);
            case WiredBoxType.ConditionTriggererOnFurni:
                return new TriggererOnFurniBox(_room, item);
            case WiredBoxType.ConditionTriggererNotOnFurni:
                return new TriggererNotOnFurniBox(_room, item);
            case WiredBoxType.ConditionFurniHasNoUsers:
                return new FurniHasNoUsersBox(_room, item);
            case WiredBoxType.ConditionFurniHasFurni:
                return new FurniHasFurniBox(_room, item);
            case WiredBoxType.ConditionIsGroupMember:
                return new IsGroupMemberBox(_room, item);
            case WiredBoxType.ConditionIsNotGroupMember:
                return new IsNotGroupMemberBox(_room, item);
            case WiredBoxType.ConditionUserCountInRoom:
                return new UserCountInRoomBox(_room, item);
            case WiredBoxType.ConditionUserCountDoesntInRoom:
                return new UserCountDoesntInRoomBox(_room, item);
            case WiredBoxType.ConditionIsWearingFx:
                return new IsWearingFxBox(_room, item);
            case WiredBoxType.ConditionIsNotWearingFx:
                return new IsNotWearingFxBox(_room, item);
            case WiredBoxType.ConditionIsWearingBadge:
                return new IsWearingBadgeBox(_room, item);
            case WiredBoxType.ConditionIsNotWearingBadge:
                return new IsNotWearingBadgeBox(_room, item);
            case WiredBoxType.ConditionMatchStateAndPosition:
                return new FurniMatchStateAndPositionBox(_room, item);
            case WiredBoxType.ConditionDontMatchStateAndPosition:
                return new FurniDoesntMatchStateAndPositionBox(_room, item);
            case WiredBoxType.ConditionFurniHasNoFurni:
                return new FurniHasNoFurniBox(_room, item);
            case WiredBoxType.ConditionActorHasHandItemBox:
                return new ActorHasHandItemBox(_room, item);
            case WiredBoxType.ConditionActorIsInTeamBox:
                return new ActorIsInTeamBox(_room, item);
            /*
            case WiredBoxType.ConditionMatchStateAndPosition:
                return new FurniMatchStateAndPositionBox(_room, Item);

            case WiredBoxType.ConditionFurniTypeMatches:
                return new FurniTypeMatchesBox(_room, Item);
            case WiredBoxType.ConditionFurniTypeDoesntMatch:
                return new FurniTypeDoesntMatchBox(_room, Item);
            case WiredBoxType.ConditionFurniHasNoFurni:
                return new FurniHasNoFurniBox(_room, Item);*/
            case WiredBoxType.AddonRandomEffect:
                return new AddonRandomEffectBox(_room, item);
            case WiredBoxType.EffectMoveFurniToNearestUser:
                return new MoveFurniToUserBox(_room, item);
            case WiredBoxType.EffectExecuteWiredStacks:
                return new ExecuteWiredStacksBox(_room, item);
            case WiredBoxType.EffectTeleportBotToFurniBox:
                return new TeleportBotToFurniBox(_room, item);
            case WiredBoxType.EffectBotChangesClothesBox:
                return new BotChangesClothesBox(_room, item, _botStore);
            case WiredBoxType.EffectBotMovesToFurniBox:
                return new BotMovesToFurniBox(_room, item);
            case WiredBoxType.EffectBotCommunicatesToAllBox:
                return new BotCommunicatesToAllBox(_room, item);
            case WiredBoxType.EffectBotGivesHanditemBox:
                return new BotGivesHandItemBox(_room, item);
            case WiredBoxType.EffectBotFollowsUserBox:
                return new BotFollowsUserBox(_room, item);
            case WiredBoxType.EffectSetRollerSpeed:
                return new SetRollerSpeedBox(_room, item);
            case WiredBoxType.EffectRegenerateMaps:
                return new RegenerateMapsBox(_room, item);
            case WiredBoxType.EffectGiveUserBadge:
                return new GiveUserBadgeBox(_room, item, _access);
        }

        return null;
    }

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

    public void SaveBox(IWiredItem item)
    {
        var items = "";
        IWiredCycle? cycle = null;

        if (item is IWiredCycle) {
            cycle = (IWiredCycle)item;
        }

        foreach (var I in item.SetItems.Values) {
            var selectedItem = _room.GetRoomItemHandler().GetItem(Convert.ToUInt32(I.Id));

            if (selectedItem == null) {
                continue;
            }

            if (item.Type == WiredBoxType.EffectMatchPosition || item.Type == WiredBoxType.ConditionMatchStateAndPosition || item.Type == WiredBoxType.ConditionDontMatchStateAndPosition) {
                items += $"{I.Id}:{I.GetX},{I.GetY},{I.GetZ},{I.Rotation},{I.LegacyDataString};";
            }
            else {
                items += $"{I.Id};";
            }
        }

        if (item.Type == WiredBoxType.EffectMatchPosition || item.Type == WiredBoxType.ConditionMatchStateAndPosition || item.Type == WiredBoxType.ConditionDontMatchStateAndPosition) {
            item.ItemsData = items;
        }

        using var connection = _database.Connection();
        connection.Execute("REPLACE INTO wired_items (id,items,delay,`string`,`bool`) VALUES (@id,@items,@delay,@stringData,@boolData)",
            new { id = item.Item.Id, items, delay = item is IWiredCycle ? cycle.Delay : 0, stringData = item.StringData, boolData = item.BoolData });
        _engine.CancelPending(item);
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

    private sealed class WiredItemRow
    {
        public string Items { get; init; } = "";
        public int Delay { get; init; }
        public string StringData { get; init; } = "";
        public bool BoolData { get; init; }
    }

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
