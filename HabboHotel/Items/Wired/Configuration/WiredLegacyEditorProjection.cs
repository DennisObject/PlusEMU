using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Addons;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Current editor metadata for existing legacy boxes, including deployments with custom furniture names.</summary>
public static class WiredLegacyEditorProjection
{
    public static bool TryGetConfiguration(IWiredItem original, out WiredBoxDescriptor descriptor,
        out WiredConfiguration configuration)
    {
        if (WiredLegacyCustomEditor.IsCustom(original))
            return WiredLegacyCustomEditor.TryGetConfiguration(original, out descriptor, out configuration);
        configuration = new();
        return TryGetDescriptor(original, out descriptor)
            && (WiredLegacyAddonConfigurationAdapter.TryConvert(original, descriptor, out configuration)
                || WiredLegacyConfigurationAdapter.TryConvert(original, descriptor, out configuration));
    }

    public static bool TryGetDescriptor(IWiredItem original, out WiredBoxDescriptor descriptor)
    {
        descriptor = null!;
        if (WiredLegacyCustomEditor.IsCustom(original))
            return false;
        if (original.Item.Definition.WiredDescriptor is { } named)
        {
            descriptor = named;
            return true;
        }
        var name = original.Type switch
        {
            WiredBoxType.TriggerRoomEnter => "wf_trg_enter_room",
            WiredBoxType.TriggerUserSays or WiredBoxType.TriggerUserSaysCommand => "wf_trg_says_something",
            WiredBoxType.TriggerRepeat => "wf_trg_periodically",
            WiredBoxType.TriggerStateChanges => "wf_trg_stuff_state",
            WiredBoxType.TriggerWalkOnFurni => "wf_trg_walks_on_furni",
            WiredBoxType.TriggerWalkOffFurni => "wf_trg_walks_off_furni",
            WiredBoxType.TriggerGameStarts => "wf_trg_game_starts",
            WiredBoxType.TriggerGameEnds => "wf_trg_game_ends",
            WiredBoxType.TriggerUserFurniCollision => "wf_trg_collision",
            WiredBoxType.EffectShowMessage => "wf_act_show_message",
            WiredBoxType.EffectTeleportToFurni => "wf_act_teleport_to",
            WiredBoxType.EffectToggleFurniState => "wf_act_toggle_state",
            WiredBoxType.EffectKickUser => "wf_act_kick_user",
            WiredBoxType.EffectMatchPosition => "wf_act_match_to_sshot",
            WiredBoxType.EffectMoveAndRotate => "wf_act_move_rotate",
            WiredBoxType.EffectMoveFurniToNearestUser => "wf_act_chase",
            WiredBoxType.EffectMoveFurniFromNearestUser => "wf_act_flee",
            WiredBoxType.EffectMuteTriggerer => "wf_act_mute_triggerer",
            WiredBoxType.EffectGiveReward => "wf_act_give_reward",
            WiredBoxType.EffectExecuteWiredStacks => "wf_act_call_stacks",
            WiredBoxType.EffectTeleportBotToFurniBox => "wf_act_bot_teleport",
            WiredBoxType.EffectBotChangesClothesBox => "wf_act_bot_clothes",
            WiredBoxType.EffectBotMovesToFurniBox => "wf_act_bot_move",
            WiredBoxType.EffectBotCommunicatesToAllBox => "wf_act_bot_talk",
            WiredBoxType.EffectBotCommunicatesToUserBox => "wf_act_bot_talk_to_avatar",
            WiredBoxType.EffectBotFollowsUserBox => "wf_act_bot_follow_avatar",
            WiredBoxType.EffectBotGivesHanditemBox => "wf_act_bot_give_handitem",
            WiredBoxType.EffectAddActorToTeam => "wf_act_join_team",
            WiredBoxType.EffectRemoveActorFromTeam => "wf_act_leave_team",
            WiredBoxType.ConditionFurniHasUsers => "wf_cnd_furnis_hv_avtrs",
            WiredBoxType.ConditionFurniHasFurni => "wf_cnd_has_furni_on",
            WiredBoxType.ConditionTriggererOnFurni => "wf_cnd_trggrer_on_frn",
            WiredBoxType.ConditionIsGroupMember => "wf_cnd_actor_in_group",
            WiredBoxType.ConditionIsNotGroupMember => "wf_cnd_not_in_group",
            WiredBoxType.ConditionTriggererNotOnFurni => "wf_cnd_not_trggrer_on",
            WiredBoxType.ConditionFurniHasNoUsers => "wf_cnd_not_hv_avtrs",
            WiredBoxType.ConditionIsWearingBadge => "wf_cnd_wearing_badge",
            WiredBoxType.ConditionIsWearingFx => "wf_cnd_wearing_effect",
            WiredBoxType.ConditionIsNotWearingBadge => "wf_cnd_not_wearing_b",
            WiredBoxType.ConditionIsNotWearingFx => "wf_cnd_not_wearing_fx",
            WiredBoxType.ConditionMatchStateAndPosition => "wf_cnd_match_snapshot",
            WiredBoxType.ConditionDontMatchStateAndPosition => "wf_cnd_not_match_snap",
            WiredBoxType.ConditionUserCountInRoom => "wf_cnd_user_count_in",
            WiredBoxType.ConditionUserCountDoesntInRoom => "wf_cnd_not_user_count",
            WiredBoxType.ConditionFurniHasNoFurni => "wf_cnd_not_furni_on",
            WiredBoxType.ConditionActorHasHandItemBox => "wf_cnd_has_handitem",
            WiredBoxType.ConditionActorIsInTeamBox => "wf_cnd_actor_in_team",
            WiredBoxType.AddonRandomEffect => "wf_xtra_random",
            _ => null
        };
        return WiredBoxRegistry.TryGet(name, out descriptor);
    }
}
