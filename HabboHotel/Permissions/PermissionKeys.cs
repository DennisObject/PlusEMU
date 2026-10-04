using System.Reflection;

namespace Plus.HabboHotel.Permissions;

public sealed record PermissionDefinition(string Key, string Category, string Description);

// The registry is the universe used to expand wildcards and to flag stale database keys.
public static class PermissionKeys
{
    public const string ClubAccess = "club.access";
    public const string Ambassador = "ambassador";
    public const string AvatarNameStaffPrefixRequired = "avatar.name.staff_prefix_required";
    public const string BotEditAnyOverride = "bot.edit_any_override";
    public const string BotPlaceAnyOverride = "bot.place_any_override";
    public const string CameraUse = "camera.use";
    public const string CampaignCalendarForce = "campaign.calendar.force";
    public const string CatalogEdit = "catalog.edit";
    public const string CatalogGiftStaff = "catalog.gift.staff";
    public const string ChatFilterBypass = "chat.filter_bypass";
    public const string ChatReportUnlimited = "chat.report.unlimited";
    public const string ChatStyleStaff = "chat.style.staff";
    public const string CommandAbout = "command.about";
    public const string CommandAlert = "command.alert";
    public const string CommandAllaroundme = "command.allaroundme";
    public const string CommandAlleyesonme = "command.alleyesonme";
    public const string CommandBan = "command.ban";
    public const string CommandBubble = "command.bubble";
    public const string CommandCarry = "command.carry";
    public const string CommandConvertcredits = "command.convertcredits";
    public const string CommandCoords = "command.coords";
    public const string CommandDance = "command.dance";
    public const string CommandDc = "command.dc";
    public const string CommandDeletegroup = "command.deletegroup";
    public const string CommandDisablediagonal = "command.disablediagonal";
    public const string CommandDisablegifts = "command.disablegifts";
    public const string CommandDisablemimic = "command.disablemimic";
    public const string CommandDisablewhispers = "command.disablewhispers";
    public const string CommandDnd = "command.dnd";
    public const string CommandEha = "command.eha";
    public const string CommandEjectall = "command.ejectall";
    public const string CommandEmptyitems = "command.emptyitems";
    public const string CommandEnable = "command.enable";
    public const string CommandFaceless = "command.faceless";
    public const string CommandFastwalk = "command.fastwalk";
    public const string CommandFlagme = "command.flagme";
    public const string CommandFlaguser = "command.flaguser";
    public const string CommandFollow = "command.follow";
    public const string CommandForcedEffects = "command.forced_effects";
    public const string CommandForcesit = "command.forcesit";
    public const string CommandFreeze = "command.freeze";
    public const string CommandGive = "command.give";
    public const string CommandGivebadge = "command.givebadge";
    public const string CommandGoto = "command.goto";
    public const string CommandHa = "command.ha";
    public const string CommandHal = "command.hal";
    public const string CommandIgnorewhispers = "command.ignorewhispers";
    public const string CommandIpban = "command.ipban";
    public const string CommandKick = "command.kick";
    public const string CommandKickbots = "command.kickbots";
    public const string CommandKickpets = "command.kickpets";
    public const string CommandLay = "command.lay";
    public const string CommandMakesay = "command.makesay";
    public const string CommandMassbadge = "command.massbadge";
    public const string CommandMassdance = "command.massdance";
    public const string CommandMassenable = "command.massenable";
    public const string CommandMimic = "command.mimic";
    public const string CommandMip = "command.mip";
    public const string CommandMoonwalk = "command.moonwalk";
    public const string CommandMute = "command.mute";
    public const string CommandMutebots = "command.mutebots";
    public const string CommandMutepets = "command.mutepets";
    public const string CommandOverride = "command.override";
    public const string CommandOverrideMassenable = "command.override_massenable";
    public const string CommandPet = "command.pet";
    public const string CommandPickall = "command.pickall";
    public const string CommandPull = "command.pull";
    public const string CommandPush = "command.push";
    public const string CommandRegenmaps = "command.regenmaps";
    public const string CommandRoom = "command.room";
    public const string CommandRoomalert = "command.roomalert";
    public const string CommandRoombadge = "command.roombadge";
    public const string CommandRoomkick = "command.roomkick";
    public const string CommandRoommute = "command.roommute";
    public const string CommandRoomunmute = "command.roomunmute";
    public const string CommandSa = "command.sa";
    public const string CommandSellroom = "command.sellroom";
    public const string CommandSetmax = "command.setmax";
    public const string CommandSetspeed = "command.setspeed";
    public const string CommandSit = "command.sit";
    public const string CommandSpull = "command.spull";
    public const string CommandSpush = "command.spush";
    public const string CommandStand = "command.stand";
    public const string CommandStats = "command.stats";
    public const string CommandStress = "command.stress";
    public const string CommandSummon = "command.summon";
    public const string CommandSuperfastwalk = "command.superfastwalk";
    public const string CommandTeleport = "command.teleport";
    public const string CommandTradeban = "command.tradeban";
    public const string CommandUnfreeze = "command.unfreeze";
    public const string CommandUnload = "command.unload";
    public const string CommandUnmute = "command.unmute";
    public const string CommandUpdate = "command.update";
    public const string CommandUserinfo = "command.userinfo";
    public const string FortuneWheelManage = "fortune_wheel.manage";
    public const string FurniDelete = "furni.delete";
    public const string FurniEdit = "furni.edit";
    public const string GroupAcceptAny = "group.accept.any";
    public const string GroupDeleteLimitOverride = "group.delete_limit_override";
    public const string GroupDeleteOverride = "group.delete_override";
    public const string GroupManagementOverride = "group.management_override";
    public const string HousekeepingAccess = "housekeeping.access";
    public const string HousekeepingAlert = "housekeeping.alert";
    public const string HousekeepingEconomy = "housekeeping.economy";
    public const string HousekeepingPassword = "housekeeping.password";
    public const string HousekeepingPrivateData = "housekeeping.private_data";
    public const string HousekeepingRolesManage = "housekeeping.roles.manage";
    public const string HousekeepingRoomOwnership = "housekeeping.room_ownership";
    public const string HousekeepingRooms = "housekeeping.rooms";
    public const string HousekeepingSanction = "housekeeping.sanction";
    public const string ModerationAlert = "moderation.alert";
    public const string ModerationBan = "moderation.ban";
    public const string ModerationBanSoft = "moderation.ban.soft";
    public const string ModerationCaution = "moderation.caution";
    public const string ModerationDisconnectAny = "moderation.disconnect_any";
    public const string ModerationIpBan = "moderation.ip_ban";
    public const string ModerationKick = "moderation.kick";
    public const string ModerationKickAny = "moderation.kick_any";
    public const string ModerationMachineBan = "moderation.machine_ban";
    public const string ModerationMakeSayAny = "moderation.make_say_any";
    public const string ModerationMute = "moderation.mute";
    public const string ModerationMuteAny = "moderation.mute.any";
    public const string ModerationMuteLimitOverride = "moderation.mute_limit_override";
    public const string ModerationRoomAlert = "moderation.room_alert";
    public const string ModerationTickets = "moderation.tickets";
    public const string ModerationTool = "moderation.tool";
    public const string ModerationTradeLock = "moderation.trade_lock";
    public const string ModerationTradeLockAny = "moderation.trade_lock_any";
    public const string NavigatorCategoriesStaff = "navigator.categories.staff";
    public const string NavigatorEventsModerate = "navigator.events.moderate";
    public const string NavigatorRoomModelsStaff = "navigator.room_models.staff";
    public const string NavigatorStaffPick = "navigator.staff_pick";
    public const string RewardtrackManage = "rewardtrack.manage";
    public const string RoomBanOverride = "room.ban_override";
    public const string RoomDeleteAny = "room.delete_any";
    public const string RoomDiceCloseAny = "room.dice.close_any";
    public const string RoomEnterFull = "room.enter_full";
    public const string RoomEnterLocked = "room.enter_locked";
    public const string RoomIgnoreMute = "room.ignore_mute";
    public const string RoomItemPlaceExchangeAnywhere = "room.item_place_exchange_anywhere";
    public const string RoomItemSaveBrandingItems = "room.item_save_branding_items";
    public const string RoomItemTake = "room.item_take";
    public const string RoomItemUseAnyStackTile = "room.item_use_any_stack_tile";
    public const string RoomItemWiredRewards = "room.item_wired_rewards";
    public const string RoomOverrideCustomConfig = "room.override_custom_config";
    public const string RoomOwnerAny = "room.owner.any";
    public const string RoomRightsAny = "room.rights.any";
    public const string RoomTradeOverride = "room.trade_override";
    public const string RoomUnloadAny = "room.unload_any";
    public const string RoomUserLimitOverride = "room.user_limit.override";
    public const string RoomWhisperOverride = "room.whisper_override";
    public const string RoomYoutubeControlAny = "room.youtube.control_any";
    public const string StaffEvents = "staff.events";
    public const string StaffIgnoreAdvertisementReports = "staff.ignore_advertisement_reports";
    public const string StaffIgnoreModAlert = "staff.ignore_mod_alert";
    public const string StaffReceiveAlerts = "staff.receive_alerts";

    public const string CommandUpdateAchievements = "command.update_achievements";

    public const string CommandUpdateAntiMutant = "command.update_anti_mutant";

    public const string CommandUpdateBadgeDefinitions = "command.update_badge_definitions";

    public const string CommandUpdateBans = "command.update_bans";

    public const string CommandUpdateBots = "command.update_bots";

    public const string CommandUpdateCatalog = "command.update_catalog";

    public const string CommandUpdateChatStyles = "command.update_chat_styles";

    public const string CommandUpdateConfiguration = "command.update_configuration";

    public const string CommandUpdateFilter = "command.update_filter";

    public const string CommandUpdateFurni = "command.update_furni";

    public const string CommandUpdateGameCenter = "command.update_game_center";

    public const string CommandUpdateLocale = "command.update_locale";

    public const string CommandUpdateModels = "command.update_models";

    public const string CommandUpdateModeration = "command.update_moderation";

    public const string CommandUpdateNavigator = "command.update_navigator";

    public const string CommandUpdatePetLocale = "command.update_pet_locale";

    public const string CommandUpdatePromotions = "command.update_promotions";

    public const string CommandUpdateQuests = "command.update_quests";

    public const string CommandUpdateRewards = "command.update_rewards";

    public const string CommandUpdateRights = "command.update_rights";

    public const string CommandUpdateVouchers = "command.update_vouchers";

    public const string CommandUpdateYoutube = "command.update_youtube";

    public const string CommandGiveCoins = "command.give_coins";

    public const string CommandGivePixels = "command.give_pixels";

    public const string CommandGiveDiamonds = "command.give_diamonds";

    public const string CommandGiveGotw = "command.give_gotw";

    public static IReadOnlyList<PermissionDefinition> All { get; } = typeof(PermissionKeys)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => Describe((string)field.GetRawConstantValue()!)).OrderBy(permission => permission.Key, StringComparer.Ordinal).ToArray();

    // Threshold gates are named after configurable roles, so their concrete keys are generated here.
    public static IEnumerable<PermissionDefinition> ForRoles(IEnumerable<string> slugs) => slugs
        .SelectMany(slug => new[] { $"catalog.pages.{slug}", $"navigator.searches.{slug}" })
        .Select(Describe);

    private static PermissionDefinition Describe(string key) => new(key, key.Split('.')[0],
        $"Allows {key.Replace('.', ' ').Replace('_', ' ')}.");
}
