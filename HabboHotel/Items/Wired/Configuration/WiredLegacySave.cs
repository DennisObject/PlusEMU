using System.Collections.Concurrent;
using Plus.Communication.Flash;
using Plus.HabboHotel.GameClients;

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
        if (original.Type == WiredBoxType.TriggerRepeat
            && proposed.IntParams[0] is < 0 or > WiredConfigurationLimits.DelayPulses)
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
