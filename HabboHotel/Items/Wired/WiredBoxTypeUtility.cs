namespace Plus.HabboHotel.Items.Wired;

internal static class WiredBoxTypeUtility
{
    public static WiredBoxType FromWiredId(int id)
    {
        switch (id)
        {
            default:
                return WiredBoxType.None;
            case 1:
                return WiredBoxType.TriggerUserSays;
            case 2:
                return WiredBoxType.TriggerStateChanges;
            case 3:
                return WiredBoxType.TriggerRepeat;
            case 4:
                return WiredBoxType.TriggerRoomEnter;
            case 8:
                return WiredBoxType.TriggerWalkOnFurni;
            case 9:
                return WiredBoxType.TriggerWalkOffFurni;
            case 5:
                return WiredBoxType.EffectShowMessage;
            case 6:
                return WiredBoxType.EffectTeleportToFurni;
            case 7:
                return WiredBoxType.EffectToggleFurniState;
            case 10:
                return WiredBoxType.EffectKickUser;
            case 11:
                return WiredBoxType.ConditionFurniHasUsers;
            case 12:
                return WiredBoxType.ConditionFurniHasFurni;
            case 13:
                return WiredBoxType.ConditionTriggererOnFurni;
            case 14:
                return WiredBoxType.EffectMatchPosition;
            case 21:
                return WiredBoxType.ConditionIsGroupMember;
            case 22:
                return WiredBoxType.ConditionIsNotGroupMember;
            case 23:
                return WiredBoxType.ConditionTriggererNotOnFurni;
            case 24:
                return WiredBoxType.ConditionFurniHasNoUsers;
            case 25:
                return WiredBoxType.ConditionIsWearingBadge;
            case 26:
                return WiredBoxType.ConditionIsWearingFx;
            case 27:
                return WiredBoxType.ConditionIsNotWearingBadge;
            case 28:
                return WiredBoxType.ConditionIsNotWearingFx;
            case 29:
                return WiredBoxType.ConditionMatchStateAndPosition;
            case 30:
                return WiredBoxType.ConditionUserCountInRoom;
            case 31:
                return WiredBoxType.ConditionUserCountDoesntInRoom;
            case 32:
                return WiredBoxType.EffectMoveAndRotate;
            case 33:
                return WiredBoxType.ConditionDontMatchStateAndPosition;
            case 34:
                return WiredBoxType.ConditionFurniTypeMatches;
            case 35:
                return WiredBoxType.ConditionFurniTypeDoesntMatch;
            case 36:
                return WiredBoxType.ConditionFurniHasNoFurni;
            case 37:
                return WiredBoxType.EffectMoveFurniToNearestUser;
            case 38:
                return WiredBoxType.EffectMoveFurniFromNearestUser;
            case 39:
                return WiredBoxType.EffectMuteTriggerer;
            case 40:
                return WiredBoxType.EffectGiveReward;
            case 41:
                return WiredBoxType.AddonRandomEffect;
            case 42:
                return WiredBoxType.TriggerGameStarts;
            case 43:
                return WiredBoxType.TriggerGameEnds;
            case 44:
                return WiredBoxType.TriggerUserFurniCollision;
            case 45:
                return WiredBoxType.EffectMoveFurniToNearestUser;
            case 46:
                return WiredBoxType.EffectExecuteWiredStacks;
            case 47:
                return WiredBoxType.EffectTeleportBotToFurniBox;
            case 48:
                return WiredBoxType.EffectBotChangesClothesBox;
            case 49:
                return WiredBoxType.EffectBotMovesToFurniBox;
            case 50:
                return WiredBoxType.EffectBotCommunicatesToAllBox;
            case 51:
                return WiredBoxType.EffectBotCommunicatesToUserBox;
            case 52:
                return WiredBoxType.EffectBotFollowsUserBox;
            case 53:
                return WiredBoxType.EffectBotGivesHanditemBox;
            case 54:
                return WiredBoxType.ConditionActorHasHandItemBox;
            case 55:
                return WiredBoxType.ConditionActorIsInTeamBox;
            case 56:
                return WiredBoxType.EffectAddActorToTeam;
            case 57:
                return WiredBoxType.EffectRemoveActorFromTeam;
            case 58:
                return WiredBoxType.TriggerUserSaysCommand;
            case 59:
                return WiredBoxType.EffectSetRollerSpeed;
            case 60:
                return WiredBoxType.EffectRegenerateMaps;
            case 61:
                return WiredBoxType.EffectGiveUserBadge;
        }
    }

    // Matches the existing GenerateNewBox switch; declared but absent boxes cannot force legacy classification.
    public static bool IsLegacyConstructible(WiredBoxType type) => Enum.IsDefined(type)
        && type is not (WiredBoxType.None or WiredBoxType.ConditionFurniTypeMatches
            or WiredBoxType.ConditionFurniTypeDoesntMatch or WiredBoxType.EffectMoveFurniFromNearestUser
            or WiredBoxType.EffectBotCommunicatesToUserBox);

    // Editor codes belong to the envelope; they are not furniture.wired_id values.
    public static int GetWiredId(WiredBoxType type) => type switch
    {
        WiredBoxType.TriggerUserSays => 0,
        WiredBoxType.TriggerUserSaysCommand => 0,
        WiredBoxType.TriggerWalkOnFurni => 1,
        WiredBoxType.TriggerWalkOffFurni => 2,
        WiredBoxType.TriggerStateChanges => 4,
        WiredBoxType.TriggerRepeat => 6,
        WiredBoxType.TriggerRoomEnter => 7,
        WiredBoxType.TriggerGameStarts => 8,
        WiredBoxType.TriggerGameEnds => 9,
        WiredBoxType.TriggerUserFurniCollision => 11,
        WiredBoxType.EffectToggleFurniState => 0,
        WiredBoxType.EffectMatchPosition => 3,
        WiredBoxType.EffectMoveAndRotate => 4,
        WiredBoxType.EffectShowMessage => 7,
        WiredBoxType.EffectTeleportToFurni => 8,
        WiredBoxType.EffectAddActorToTeam => 9,
        WiredBoxType.EffectRemoveActorFromTeam => 10,
        WiredBoxType.EffectMoveFurniToNearestUser => 11,
        WiredBoxType.EffectMoveFurniFromNearestUser => 12,
        WiredBoxType.EffectGiveReward => 17,
        WiredBoxType.EffectExecuteWiredStacks => 18,
        WiredBoxType.EffectKickUser => 19,
        WiredBoxType.EffectMuteTriggerer => 20,
        WiredBoxType.EffectTeleportBotToFurniBox => 21,
        WiredBoxType.EffectBotMovesToFurniBox => 22,
        WiredBoxType.EffectBotCommunicatesToAllBox => 23,
        WiredBoxType.EffectBotGivesHanditemBox => 24,
        WiredBoxType.EffectBotFollowsUserBox => 25,
        WiredBoxType.EffectBotChangesClothesBox => 26,
        WiredBoxType.EffectBotCommunicatesToUserBox => 27,
        WiredBoxType.EffectSetRollerSpeed => 88,
        WiredBoxType.EffectRegenerateMaps => 123,
        WiredBoxType.EffectGiveUserBadge => 119,
        WiredBoxType.ConditionMatchStateAndPosition => 0,
        WiredBoxType.ConditionFurniHasUsers => 1,
        WiredBoxType.ConditionTriggererOnFurni => 2,
        WiredBoxType.ConditionUserCountInRoom => 5,
        WiredBoxType.ConditionActorIsInTeamBox => 6,
        WiredBoxType.ConditionFurniHasFurni => 7,
        WiredBoxType.ConditionFurniTypeMatches => 8,
        WiredBoxType.ConditionIsGroupMember => 10,
        WiredBoxType.ConditionIsWearingBadge => 11,
        WiredBoxType.ConditionIsWearingFx => 12,
        WiredBoxType.ConditionDontMatchStateAndPosition => 13,
        WiredBoxType.ConditionFurniHasNoUsers => 14,
        WiredBoxType.ConditionTriggererNotOnFurni => 15,
        WiredBoxType.ConditionUserCountDoesntInRoom => 16,
        WiredBoxType.ConditionFurniHasNoFurni => 18,
        WiredBoxType.ConditionFurniTypeDoesntMatch => 19,
        WiredBoxType.ConditionIsNotGroupMember => 21,
        WiredBoxType.ConditionIsNotWearingBadge => 22,
        WiredBoxType.ConditionIsNotWearingFx => 23,
        WiredBoxType.ConditionActorHasHandItemBox => 25,
        WiredBoxType.AddonRandomEffect => 63,
        _ => 0
    };

    public static List<int> ContainsBlockedTrigger(IWiredItem box, ICollection<IWiredItem> triggers)
    {
        var blockedItems = new List<int>();
        if (box.Type != WiredBoxType.EffectShowMessage && box.Type != WiredBoxType.EffectMuteTriggerer && box.Type != WiredBoxType.EffectTeleportToFurni && box.Type != WiredBoxType.EffectKickUser &&
            box.Type != WiredBoxType.ConditionTriggererOnFurni)
            return blockedItems;
        foreach (var item in triggers)
        {
            if (item.Type == WiredBoxType.TriggerRepeat)
            {
                if (!blockedItems.Contains(item.Item.Definition.SpriteId))
                    blockedItems.Add(item.Item.Definition.SpriteId);
                else continue;
            }
            else continue;
        }
        return blockedItems;
    }

    public static List<int> ContainsBlockedEffect(IWiredItem box, ICollection<IWiredItem> effects)
    {
        var blockedItems = new List<int>();
        if (box.Type != WiredBoxType.TriggerRepeat)
            return blockedItems;
        var hasMoveRotate = effects.Where(x => x.Type == WiredBoxType.EffectMoveAndRotate).ToList().Count > 0;
        var hasMoveNear = effects.Where(x => x.Type == WiredBoxType.EffectMoveFurniToNearestUser).ToList().Count > 0;
        foreach (var item in effects)
        {
            if (item.Type == WiredBoxType.EffectKickUser || item.Type == WiredBoxType.EffectMuteTriggerer || item.Type == WiredBoxType.EffectShowMessage ||
                item.Type == WiredBoxType.EffectTeleportToFurni || item.Type == WiredBoxType.EffectBotFollowsUserBox)
            {
                if (!blockedItems.Contains(item.Item.Definition.SpriteId))
                    blockedItems.Add(item.Item.Definition.SpriteId);
                else continue;
            }
            else if (item.Type == WiredBoxType.EffectMoveFurniToNearestUser && hasMoveRotate || item.Type == WiredBoxType.EffectMoveAndRotate && hasMoveNear)
            {
                if (!blockedItems.Contains(item.Item.Definition.SpriteId))
                    blockedItems.Add(item.Item.Definition.SpriteId);
                else continue;
            }
        }
        return blockedItems;
    }
}