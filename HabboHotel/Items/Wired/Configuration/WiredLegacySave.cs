using System.Collections.Concurrent;
using Plus.Communication.Flash;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Parse legacy edits on a detached box; the room engine owns durable publication.</summary>
public static class WiredLegacySave
{
    private const int MaximumPayloadBytes = 4 + 4 * WiredConfigurationLimits.IntParams + 2 + ushort.MaxValue
        + 4 + 4 * WiredConfigurationLimits.SelectedItems + 8;

    // The caller has read item id and checked rights/category. The factory must not register the candidate.
    public static bool TryPrepare(IWiredItem original, IIncomingPacket packet, WiredBoxCategory envelope,
        Func<IWiredItem, IWiredItem?> createCandidate, out IWiredItem? candidate, out string error,
        Func<uint, bool>? existsInRoom = null)
    {
        candidate = null;
        error = "Invalid legacy Wired settings.";
        if (original is IWiredConfiguredItem || original.Type == WiredBoxType.None
            || packet.Buffer.Length > MaximumPayloadBytes)
            return false;
        // Flash reads reverse bytes in place, so preserve the original fields before parsing.
        var replay = packet.Buffer.ToArray();
        if (!WiredLegacyProtocol.TryRead(packet, envelope, out var proposed)
            || proposed.IntParams.Length != ExpectedIntCount(original.Type)
            || existsInRoom != null && !proposed.SelectedItems.All(existsInRoom))
            return false;
        if (original.Type is WiredBoxType.EffectMatchPosition or WiredBoxType.ConditionMatchStateAndPosition
                or WiredBoxType.ConditionDontMatchStateAndPosition
            && proposed.IntParams.Any(value => value is < 0 or > 1))
            return false;
        if (original.Type == WiredBoxType.TriggerRepeat && !IsRepeaterDelay(proposed.IntParams[0]))
            return false;
        if (original.Type == WiredBoxType.EffectSetRollerSpeed && !int.TryParse(proposed.Text, out _))
            return false;
        try
        {
            var detached = createCandidate(original);
            if (detached == null || ReferenceEquals(detached, original) || detached is IWiredConfiguredItem
                || detached.Type != original.Type || !ReferenceEquals(detached.Item, original.Item)
                || !ReferenceEquals(detached.Instance, original.Instance))
                return false;
            detached.StringData = original.StringData;
            detached.BoolData = original.BoolData;
            detached.ItemsData = original.ItemsData;
            detached.SetItems = new ConcurrentDictionary<uint, Item>(original.SetItems);
            if (original is IWiredCycle current && detached is IWiredCycle next)
                next.Delay = current.Delay;
            using var replayStream = PlusMemoryStream.GetStream(replay);
            detached.HandleSave(new FlashIncomingPacket(replayStream));
            // Some legacy parsers intentionally consume only their prefix. The whole envelope was validated above.
            candidate = detached;
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or FormatException
            or OverflowException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool TrySave(IWiredItem original, IIncomingPacket packet, WiredBoxCategory envelope,
        Func<IWiredItem, IWiredItem?> createCandidate, Func<IWiredItem, IWiredItem, bool> persistAndPublish,
        out string error, Func<uint, bool>? existsInRoom = null)
    {
        if (!TryPrepare(original, packet, envelope, createCandidate, out var candidate, out error, existsInRoom))
            return false;
        // No box lock: the callback acquires the engine lock, checks attachment, persists, cancels, then copies.
        if (!persistAndPublish(original, candidate!))
        {
            error = "This Wired box is no longer attached to the room.";
            return false;
        }
        return true;
    }

    public static bool TrySave(IWiredItem original, WiredConfiguration proposed, WiredBoxCategory envelope,
        Func<IWiredItem, IWiredItem?> createCandidate, Func<IWiredItem, IWiredItem, bool> persistAndPublish,
        out string error, Func<uint, bool>? existsInRoom = null)
    {
        if (!TryPrepare(original, proposed, envelope, createCandidate, out var candidate, out error, existsInRoom))
            return false;
        if (!persistAndPublish(original, candidate!))
        {
            error = "This Wired box is no longer attached to the room.";
            return false;
        }
        return true;
    }

    public static bool TryPrepare(IWiredItem original, WiredConfiguration proposed, WiredBoxCategory envelope,
        Func<IWiredItem, IWiredItem?> createCandidate, out IWiredItem? candidate, out string error,
        Func<uint, bool>? existsInRoom = null)
    {
        candidate = null;
        error = "Invalid legacy Wired settings.";
        if (original is IWiredConfiguredItem || original.Type == WiredBoxType.None
            || !WiredLegacyProtocol.IsWithinLimits(proposed)
            || proposed.IntParams.Length != ExpectedIntCount(original.Type)
            || existsInRoom != null && !proposed.SelectedItems.All(existsInRoom))
            return false;
        if (original.Type is WiredBoxType.EffectMatchPosition or WiredBoxType.ConditionMatchStateAndPosition
                or WiredBoxType.ConditionDontMatchStateAndPosition
            && proposed.IntParams.Any(value => value is < 0 or > 1))
            return false;
        if (original.Type == WiredBoxType.TriggerRepeat && !IsRepeaterDelay(proposed.IntParams[0]))
            return false;
        if (original.Type == WiredBoxType.EffectSetRollerSpeed && !int.TryParse(proposed.Text, out _))
            return false;
        try
        {
            var detached = createCandidate(original);
            if (detached == null || ReferenceEquals(detached, original) || detached is IWiredConfiguredItem
                || detached.Type != original.Type || !ReferenceEquals(detached.Item, original.Item)
                || !ReferenceEquals(detached.Instance, original.Instance))
                return false;
            detached.StringData = original.StringData;
            detached.BoolData = original.BoolData;
            detached.ItemsData = original.ItemsData;
            detached.SetItems = new ConcurrentDictionary<uint, Item>(original.SetItems);
            if (original is IWiredCycle current && detached is IWiredCycle next)
                next.Delay = current.Delay;
            ApplyConfiguration(detached, proposed);
            candidate = detached;
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or FormatException
            or OverflowException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void ApplyConfiguration(IWiredItem box, WiredConfiguration proposed)
    {
        var values = proposed.IntParams;
        switch (box.Type)
        {
            case WiredBoxType.TriggerUserSays:
            case WiredBoxType.TriggerUserSaysCommand:
                box.BoolData = values[0] == 1;
                box.StringData = proposed.Text;
                break;
            case WiredBoxType.TriggerRoomEnter:
            case WiredBoxType.EffectBotChangesClothesBox:
            case WiredBoxType.EffectBotMovesToFurniBox:
            case WiredBoxType.EffectTeleportBotToFurniBox:
            case WiredBoxType.EffectGiveUserBadge:
            case WiredBoxType.EffectKickUser:
            case WiredBoxType.EffectShowMessage:
            case WiredBoxType.EffectSetRollerSpeed:
            case WiredBoxType.ConditionIsWearingBadge:
            case WiredBoxType.ConditionIsNotWearingBadge:
                box.StringData = proposed.Text;
                break;
            case WiredBoxType.TriggerRepeat:
                ((IWiredCycle)box).Delay = values[0];
                break;
            case WiredBoxType.EffectAddActorToTeam:
            case WiredBoxType.ConditionActorIsInTeamBox:
            case WiredBoxType.ConditionActorHasHandItemBox:
            case WiredBoxType.ConditionIsWearingFx:
            case WiredBoxType.ConditionIsNotWearingFx:
                box.StringData = values[0].ToString();
                break;
            case WiredBoxType.EffectBotFollowsUserBox:
                box.StringData = $"{values[0]};{proposed.Text}";
                break;
            case WiredBoxType.EffectBotGivesHanditemBox:
                box.StringData = $"{proposed.Text};{values[0]}";
                break;
            case WiredBoxType.EffectMatchPosition:
            case WiredBoxType.ConditionMatchStateAndPosition:
            case WiredBoxType.ConditionDontMatchStateAndPosition:
                box.StringData = $"{values[0]};{values[1]};{values[2]}";
                break;
            case WiredBoxType.EffectMoveAndRotate:
            case WiredBoxType.ConditionUserCountInRoom:
            case WiredBoxType.ConditionUserCountDoesntInRoom:
                box.StringData = $"{values[0]};{values[1]}";
                break;
            case WiredBoxType.EffectMuteTriggerer:
                box.StringData = $"{values[0]};{proposed.Text}";
                break;
        }

        if (box is IWiredCycle cycle && box.Type is WiredBoxType.EffectMatchPosition
                or WiredBoxType.EffectMoveAndRotate or WiredBoxType.EffectMoveFurniToNearestUser
                or WiredBoxType.EffectTeleportToFurni or WiredBoxType.EffectToggleFurniState)
            cycle.Delay = proposed.Delay;

        if (ClearsSelections(box.Type))
            box.SetItems.Clear();
        if (!UsesSelectedItems(box.Type))
            return;
        foreach (var itemId in proposed.SelectedItems)
        {
            var item = box.Instance.GetRoomItemHandler().GetItem(itemId);
            if (item == null)
                continue;
            if (box.Type is WiredBoxType.EffectMoveAndRotate or WiredBoxType.EffectMoveFurniToNearestUser
                && box.Instance.GetWired().OtherBoxHasItem(box, item.Id))
                continue;
            box.SetItems.TryAdd(item.Id, item);
        }
    }

    private static bool UsesSelectedItems(WiredBoxType type) => type is
        WiredBoxType.TriggerStateChanges or WiredBoxType.TriggerWalkOnFurni or WiredBoxType.TriggerWalkOffFurni
        or WiredBoxType.EffectBotMovesToFurniBox or WiredBoxType.EffectExecuteWiredStacks
        or WiredBoxType.EffectMatchPosition or WiredBoxType.EffectMoveAndRotate
        or WiredBoxType.EffectMoveFurniToNearestUser or WiredBoxType.EffectTeleportBotToFurniBox
        or WiredBoxType.EffectTeleportToFurni or WiredBoxType.EffectToggleFurniState
        or WiredBoxType.ConditionMatchStateAndPosition or WiredBoxType.ConditionDontMatchStateAndPosition
        or WiredBoxType.ConditionFurniHasFurni or WiredBoxType.ConditionFurniHasNoFurni
        or WiredBoxType.ConditionFurniHasUsers or WiredBoxType.ConditionFurniHasNoUsers
        or WiredBoxType.ConditionTriggererOnFurni or WiredBoxType.ConditionTriggererNotOnFurni;

    private static bool ClearsSelections(WiredBoxType type) => UsesSelectedItems(type) || type is
        WiredBoxType.EffectBotChangesClothesBox or WiredBoxType.EffectBotCommunicatesToAllBox
        or WiredBoxType.EffectBotFollowsUserBox or WiredBoxType.EffectBotGivesHanditemBox
        or WiredBoxType.EffectGiveReward or WiredBoxType.EffectKickUser
        or WiredBoxType.EffectMuteTriggerer or WiredBoxType.EffectSetRollerSpeed;

    // The legacy repeater is wf_trg_periodically: half-second units over the same editor range.
    private static bool IsRepeaterDelay(int units) =>
        units >= 1 && units <= WiredTriggerConfiguration.MaxTimedUnits("wf_trg_periodically");

    private static int ExpectedIntCount(WiredBoxType type) => type switch
    {
        WiredBoxType.EffectMatchPosition or WiredBoxType.ConditionMatchStateAndPosition
            or WiredBoxType.ConditionDontMatchStateAndPosition => 3,
        WiredBoxType.EffectMoveAndRotate or WiredBoxType.ConditionUserCountInRoom
            or WiredBoxType.ConditionUserCountDoesntInRoom => 2,
        WiredBoxType.TriggerUserSays or WiredBoxType.TriggerUserSaysCommand or WiredBoxType.TriggerRepeat
            or WiredBoxType.EffectAddActorToTeam or WiredBoxType.EffectMuteTriggerer or WiredBoxType.EffectGiveReward
            or WiredBoxType.EffectBotFollowsUserBox or WiredBoxType.EffectBotGivesHanditemBox
            or WiredBoxType.EffectBotCommunicatesToAllBox or WiredBoxType.ConditionActorIsInTeamBox
            or WiredBoxType.ConditionActorHasHandItemBox or WiredBoxType.ConditionIsWearingFx
            or WiredBoxType.ConditionIsNotWearingFx => 1,
        _ => 0
    };
}
