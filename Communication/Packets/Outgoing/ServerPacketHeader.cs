namespace Plus.Communication.Packets.Outgoing;

public static class ServerPacketHeader
{
    // Handshake
    public const uint SimplePollStartComposer = 1429;
    public const uint SimplePollAnswerComposer = 3474;
    public const uint SimplePollAnswersComposer = 3859;
    public const uint InitDiffieHandshakeComposer = 3655;
    public const uint SecretKeyComposer = 3147;
    public const uint AuthenticationOkComposer = 1920;
    public const uint UserObjectComposer = 801;
    public const uint UserPerksComposer = 405;
    public const uint KickbackInfoComposer = 2959;
    public const uint UserRightsComposer = 2633;
    public const uint GenericErrorComposer = 1740;
    public const uint DisconnectReasonComposer = 4000;
    public const uint SetUniqueIdComposer = 65511;
    public const uint AvailabilityStatusComposer = 3100;

    // Avatar
    public const uint WardrobeComposer = 1341;

    // Catalog
    public const uint CatalogIndexComposer = 3933;
    public const uint CatalogItemDiscountComposer = 3438;
    public const uint PurchaseOKComposer = 2842;
    public const uint CatalogOfferComposer = 290;
    public const uint CatalogPageComposer = 761;
    public const uint CatalogUpdatedComposer = 592;
    public const uint HabboClubOffersComposer = 1517;
    public const uint SellablePetBreedsComposer = 2765;
    public const uint GroupFurniConfigComposer = 243;
    public const uint PresentDeliverErrorComposer = 2629;

    // Quests
    public const uint QuestListComposer = 2536;
    public const uint QuestCompletedComposer = 2105;
    public const uint QuestAbortedComposer = 1837;
    public const uint QuestStartedComposer = 1555;

    // Room Avatar
    public const uint ActionComposer = 939;
    public const uint SleepComposer = 2822;
    public const uint DanceComposer = 439;
    public const uint CarryObjectComposer = 644;
    public const uint AvatarEffectComposer = 395;

    // Room Chat
    public const uint ChatComposer = 1711;
    public const uint ShoutComposer = 2490;
    public const uint WhisperComposer = 1317;
    public const uint FloodControlComposer = 2492;
    public const uint UserTypingComposer = 2183;

    // Room Engine
    public const uint UsersComposer = 1590;
    public const uint FurnitureAliasesComposer = 3137;
    public const uint ObjectAddComposer = 1357;
    public const uint ObjectsComposer = 2852;
    public const uint ObjectUpdateComposer = 324;
    public const uint ObjectRemoveComposer = 1202;
    public const uint SlideObjectBundleComposer = 3125;
    public const uint ItemsComposer = 3506;
    public const uint ItemAddComposer = 1584;
    public const uint ItemUpdateComposer = 3820;
    public const uint ItemRemoveComposer = 3198;

    // Room Session
    public const uint RoomForwardComposer = 1314;
    public const uint RoomReadyComposer = 3589;
    public const uint OpenConnectionComposer = 1946;
    public const uint CloseConnectionComposer = 999;
    public const uint FlatAccessibleComposer = 1023;
    public const uint CantConnectComposer = 2927;

    // Room Permissions
    public const uint YouAreControllerComposer = 2909;
    public const uint YouAreNotControllerComposer = 920;
    public const uint YouAreOwnerComposer = 1624;

    // Room Settings
    public const uint RoomSettingsDataComposer = 3342;
    public const uint RoomSettingsSavedComposer = 2460;
    public const uint FlatControllerRemovedComposer = 853;
    public const uint FlatControllerAddedComposer = 1966;
    public const uint RoomRightsListComposer = 1439;

    // Room Furniture
    public const uint WiredValidationErrorComposer = 3594;
    public const uint WiredMonitorDataComposer = 5101;
    public const uint WiredRoomLogPageComposer = 65414;
    public const uint WiredRoomSettingsDataComposer = 5102;
    public const uint WiredRewardResultComposer = 1755;
    public const uint WiredMovementsComposer = 3601;
    public const uint WiredFurniMoveStyleComposer = 5110;
    public const uint WiredClickSettingsComposer = 917;
    public const uint WiredClickUserResponseComposer = 2789;
    public const uint InClientLinkComposer = 1358;
    public const uint WiredVariableFxConfigsComposer = 9473;
    public const uint WiredVariableFxConfigsRemovedComposer = 9474;
    public const uint WiredVariableFxStatusComposer = 9475;
    public const uint WiredVariableFxStatusRemovedComposer = 9476;
    public const uint WiredUserVariablesDataComposer = 5103;
    public const uint WiredAllVariablesHashComposer = 3478;
    public const uint WiredAllVariablesDiffComposer = 65496;
    public const uint WiredVariableHoldersPageComposer = 9461;
    public const uint WiredVariableHoldersComposer = 9462;
    public const uint HideWiredConfigComposer = 996;
    public const uint WiredEffectConfigComposer = 902;
    public const uint WiredConditionConfigComposer = 1773;
    public const uint WiredTriggeRconfigComposer = 868;
    public const uint MoodlightConfigComposer = 3790;
    public const uint GroupFurniSettingsComposer = 2603;
    public const uint OpenGiftComposer = 2639;

    // Navigator
    public const uint UpdateFavouriteRoomComposer = 1226;
    public const uint NavigatorLiftedRoomsComposer = 245;
    public const uint NavigatorPreferencesComposer = 2894;
    public const uint NavigatorFlatCatsComposer = 1499;
    public const uint NavigatorMetaDataParserComposer = 3745;
    public const uint NavigatorCollapsedCategoriesComposer = 2256;

    // Messenger
    public const uint BuddyListComposer = 1388;
    public const uint FriendRequestsComposer = 482;
    public const uint NewBuddyRequestComposer = 3412;

    // Moderation
    public const uint ModeratorInitComposer = 2035;
    public const uint ModeratorUserRoomVisitsComposer = 2900;
    public const uint ModeratorRoomChatlogComposer = 798;
    public const uint ModeratorUserInfoComposer = 1157;
    public const uint ModeratorSupportTicketResponseComposer = 2558;
    public const uint ModeratorUserChatlogComposer = 880;
    public const uint ModeratorRoomInfoComposer = 3966;
    public const uint ModeratorSupportTicketComposer = 344;
    public const uint ModeratorTicketChatlogComposer = 3648;
    public const uint CallForHelpPendingCallsComposer = 3558;
    public const uint CfhTopicsInitComposer = 1519;

    // Inventory
    public const uint CreditBalanceComposer = 56;
    public const uint BadgesComposer = 2564;
    public const uint FurniListAddComposer = 314;
    public const uint FurniListNotificationComposer = 1699;
    public const uint FurniListRemoveComposer = 2127;
    public const uint FurniListComposer = 3010;
    public const uint FurniListUpdateComposer = 1287;
    public const uint AvatarEffectsComposer = 699;
    public const uint AvatarEffectActivatedComposer = 2125;
    public const uint AvatarEffectExpiredComposer = 2568;
    public const uint AvatarEffectAddedComposer = 2618;
    public const uint TradingErrorComposer = 953;
    public const uint TradingAcceptComposer = 3886;
    public const uint TradingStartComposer = 1014;
    public const uint TradingUpdateComposer = 3827;
    public const uint TradingClosedComposer = 1061;
    public const uint TradingCompleteComposer = 1108;
    public const uint TradingFinishComposer = 2080;

    // Inventory Achievements
    public const uint AchievementsComposer = 1487;
    public const uint AchievementScoreComposer = 1693;
    public const uint AchievementUnlockedComposer = 2028;
    public const uint AchievementProgressedComposer = 1049;

    // Notifications
    public const uint ActivityPointsComposer = 174;
    public const uint HabboActivityPointNotificationComposer = 620;

    // Users
    public const uint ScrSendUserInfoComposer = 618;
    public const uint IgnoredUsersComposer = 2659;

    // Groups
    public const uint UnknownGroupComposer = 2990;
    public const uint GroupMembershipRequestedComposer = 317;
    public const uint ManageGroupComposer = 2507;
    public const uint HabboGroupBadgesComposer = 2661;
    public const uint NewGroupInfoComposer = 510;
    public const uint GroupInfoComposer = 1860;
    public const uint GroupCreationWindowComposer = 1914;
    public const uint SetGroupIdComposer = 2481;
    public const uint GroupMembersComposer = 3070;
    public const uint UpdateFavouriteGroupComposer = 2435;
    public const uint GroupMemberUpdatedComposer = 1116;
    public const uint GroupConfirmRemoveMemberComposer = 65505;
    public const uint GroupDeactivatedComposer = 2007;
    public const uint RefreshFavouriteGroupComposer = 2750;

    // Group Forums
    public const uint ForumsListDataComposer = 369;
    public const uint ForumDataComposer = 3878;
    public const uint ThreadCreatedComposer = 3939;
    public const uint ThreadDataComposer = 278;
    public const uint ThreadsListDataComposer = 255;
    public const uint ThreadUpdatedComposer = 828;
    public const uint ThreadReplyComposer = 2755;
    public const uint ForumsUnreadCountComposer = 1273;

    // Sound
    public const uint SoundSettingsComposer = 1501;

    public const uint AvatarAspectUpdateComposer = 1822;
    public const uint HelperToolComposer = 3539;
    public const uint RoomErrorNotifComposer = 1111;
    public const uint FollowFriendFailedComposer = 857;

    public const uint FindFriendsProcessResultComposer = 2131;
    public const uint UserChangeComposer = 3050;
    public const uint FloorHeightMapComposer = 2591;
    public const uint RoomInfoUpdatedComposer = 280;
    public const uint MessengerErrorComposer = 3114;
    public const uint MarketplaceCanMakeOfferResultComposer = 2662;
    public const uint GameAccountStatusComposer = 1323;
    public const uint GuestRoomSearchResultComposer = 3902;
    public const uint NewUserExperienceGiftOfferComposer = 3716;
    public const uint UpdateUsernameComposer = 95;
    public const uint VoucherRedeemOkComposer = 2804;
    public const uint FigureSetIdsComposer = 1523;
    public const uint StickyNoteComposer = 1238;
    public const uint UserRemoveComposer = 750;
    public const uint GetGuestRoomResultComposer = 3162;
    public const uint DoorbellComposer = 980;

    public const uint GiftWrappingConfigurationComposer = 2115;
    public const uint GetRelationshipsComposer = 1230;
    public const uint FriendNotificationComposer = 2865;
    public const uint BadgeEditorPartsComposer = 3452;
    public const uint TraxSongInfoComposer = 3591;
    public const uint PostUpdatedComposer = 2294;
    public const uint UserUpdateComposer = 399;
    public const uint MutedComposer = 1665;
    public const uint MarketplaceConfigurationComposer = 2;
    public const uint CheckGnomeNameComposer = 26;
    public const uint OpenBotActionComposer = 1546;
    public const uint FavouritesComposer = 1418;
    public const uint TalentLevelUpComposer = 1640;

    public const uint UserTagsComposer = 65514;
    public const uint CampaignComposer = 334;
    public const uint RoomEventComposer = 2283;
    public const uint MarketplaceItemStatsComposer = 108;
    public const uint HabboSearchResultComposer = 634;
    public const uint PetHorseFigureInformationComposer = 588;
    public const uint PetInventoryComposer = 3767;
    public const uint PingComposer = 1673;
    public const uint RentableSpaceComposer = 3821;
    public const uint GetYouTubePlaylistComposer = 210;
    public const uint RespectNotificationComposer = 3543;
    public const uint RecyclerPrizesComposer = 2195;
    public const uint RecyclerStatusComposer = 2869;
    public const uint RecyclerFinishedComposer = 3788;
    public const uint GetRoomBannedUsersComposer = 990;
    public const uint RoomRatingComposer = 3903;
    public const uint PlayableGamesComposer = 65482;
    public const uint TalentTrackLevelComposer = 1154;
    public const uint JoinQueueComposer = 65499;
    public const uint MarketPlaceOwnOffersComposer = 3807;
    public const uint PetBreedingComposer = 2727;
    public const uint SubmitBullyReportComposer = 3082;
    public const uint UserNameChangeComposer = 3377;
    public const uint FriendFurniStartConfirmationComposer = 3483;
    public const uint SendBullyReportComposer = 3109;
    public const uint VoucherRedeemErrorComposer = 1995;
    public const uint PurchaseErrorComposer = 894;
    public const uint CampaignCalendarDoorOpenedComposer = 744;
    public const uint FriendListUpdateComposer = 2656;

    public const uint UserFlatCatsComposer = 1464;
    public const uint UpdateFreezeLivesComposer = 1730;
    public const uint UnbanUserFromRoomComposer = 191;
    public const uint PetTrainingPanelComposer = 496;
    public const uint FlatAccessDeniedComposer = 3136;
    public const uint LatencyPingResponseComposer = 1657;
    public const uint HabboUserBadgesComposer = 822;
    public const uint HeightMapUpdateComposer = 65424;
    public const uint HeightMapComposer = 1694;

    public const uint CanCreateRoomComposer = 1185;
    public const uint InstantMessageErrorComposer = 427;
    public const uint GnomeBoxComposer = 2420;
    public const uint IgnoreStatusComposer = 1553;
    public const uint PetInformationComposer = 1988;
    public const uint NavigatorSearchResultSetComposer = 655;
    public const uint ConcurrentUsersGoalProgressComposer = 1619;
    public const uint VideoOffersRewardsComposer = 3130;
    public const uint SanctionStatusComposer = 944;
    public const uint GetYouTubeVideoComposer = 3556;
    public const uint CheckPetNameComposer = 63;
    public const uint RespectPetNotificationComposer = 2258;
    public const uint EnforceCategoryUpdateComposer = 3405;
    public const uint CommunityGoalHallOfFameComposer = 2108;
    public const uint RoomOccupiedTilesComposer = 2476;
    public const uint SendGameInvitationComposer = 65415;
    public const uint GiftWrappingErrorComposer = 3104;
    public const uint PromoArticlesComposer = 1350;
    public const uint Game1WeeklyLeaderboardComposer = 65423;
    public const uint RentableSpacesErrorComposer = 261;
    public const uint AddExperiencePointsComposer = 1285;
    public const uint GetRoomFilterListComposer = 2485;
    public const uint GameAchievementListComposer = 564;
    public const uint PromotableRoomsComposer = 778;
    public const uint RoomEntryTileComposer = 1339;
    public const uint RoomEntryInfoComposer = 1119;
    public const uint RoomNotificationComposer = 425;
    public const uint ClubGiftsComposer = 3145;
    public const uint MOTDNotificationComposer = 2214;
    public const uint PopularRoomTagsResultComposer = 269;
    public const uint NewConsoleMessageComposer = 1045;
    public const uint RoomPropertyComposer = 400;
    public const uint MarketPlaceOffersComposer = 1625;
    public const uint TalentTrackComposer = 2813;
    public const uint ProfileInformationComposer = 13;
    public const uint BadgeDefinitionsComposer = 1231;
    public const uint Game2WeeklyLeaderboardComposer = 2708;
    public const uint NameChangeUpdateComposer = 307;
    public const uint RoomVisualizationSettingsComposer = 42;
    public const uint MarketplaceMakeOfferResultComposer = 3582;
    public const uint FlatCreatedComposer = 624;
    public const uint BotInventoryComposer = 1201;
    public const uint LoadGameComposer = 65477;
    public const uint UpdateMagicTileComposer = 65486;
    public const uint MaintenanceStatusComposer = 3;
    public const uint Game3WeeklyLeaderboardComposer = 1134;
    public const uint GameListComposer = 65500;
    public const uint RoomMuteSettingsComposer = 3811;
    public const uint RoomInviteComposer = 2271;
    public const uint FriendFurniOtherLockConfirmedComposer = 3740;
    public const uint FriendFurniCancelLockComposer = 65420;
    public const uint BroadcastMessageAlertComposer = 1802;
    public const uint MarketplaceCancelOfferResultComposer = 2695;
    public const uint MarketplaceBuyOfferResultComposer = 3183;
    public const uint NavigatorSettingsComposer = 3914;

    public const uint MessengerInitComposer = 969;
    public const uint PollContentsComposer = 2663;
    public const uint PollOfferComposer = 3273;
    public const uint RoomUseHabbiconComposer = 3578;
    public const uint AllowedChatStylesComposer = 9340;
    public const uint CreatableRoomModelsComposer = 9341;
    public const uint UserHabbiconsComposer = 320;
    public const uint UserHabbiconStatusChangedComposer = 2401;
    public const uint HabbiconShopDataComposer = 1372;
    public const uint HabbiconInfoComposer = 1761;
    public const uint MessengerMessageAckComposer = 4902;
    public const uint MessengerMessageFailedComposer = 4903;
    public const uint MessengerMessageComposer = 4904;
    // Camera internal IDs; wire IDs are revision-specific.
    public const uint InitCameraComposer = 2868;
    public const uint CameraStorageUrlComposer = 3915;
    public const uint CameraPurchaseOKComposer = 1331;
    public const uint CameraPublishStatusComposer = 3473;
    public const uint CompetitionStatusComposer = 3828;
    public const uint ThumbnailStatusComposer = 941;
    public const uint RewardTracksComposer = 2693;
    public const uint RewardTrackClaimResultComposer = 3629;
    public const uint RewardTrackProgressComposer = 465;
    public const uint RewardTrackPremiumPurchaseResultComposer = 1374;
    // Octane catalog editor and furni editor; the same ids on the wire.
    public const uint FurniEditorSearchResultComposer = 10040;
    public const uint FurniEditorDetailResultComposer = 10041;
    public const uint FurniEditorInteractionsResultComposer = 10043;
    public const uint FurniEditorResultComposer = 10044;
    public const uint FurnitureDataReloadComposer = 10047;
    public const uint FurniEditorImportTextResultComposer = 10049;
    public const uint CatalogAdminResultComposer = 10059;
    public const uint CatalogAdminOfferDetailsComposer = 10062;
    public const uint CatalogAdminPageDetailsComposer = 10063;
    public const uint CatalogStudioSessionComposer = 10067;
    public const uint CatalogStudioHistoryComposer = 10071;
    public const uint CatalogStudioOperationComposer = 10072;
    // Housekeeping (in-client admin panel); internal IDs match Octane's wire IDs.
    public const uint HousekeepingUserDetailComposer = 9200;
    public const uint HousekeepingActionResultComposer = 9201;
    public const uint HousekeepingRoomDetailComposer = 9202;
    public const uint HousekeepingRoomListComposer = 9203;
    public const uint HousekeepingDashboardComposer = 9204;
    public const uint HousekeepingActionLogComposer = 9205;
    public const uint HabboClubExtendOfferComposer = 3651;
    public const uint ClubGiftReceivedComposer = 2583;
    public const uint PickMonthlyClubGiftComposer = 1628;
    public const uint HousekeepingRolesComposer = 9210;
    public const uint HousekeepingRoleMembersComposer = 9211;
    public const uint HousekeepingUserOverridesComposer = 9212;
    public const uint HousekeepingRolesAuditComposer = 9213;
    // SnowStorm (AIR Game2 payloads); internal IDs match Octane's wire IDs except where noted.
    public const uint Game2GameDirectoryStatusComposer = 2084;
    public const uint Game2GameCreatedComposer = 5000;
    public const uint Game2InArenaQueueComposer = 715;
    public const uint Game2GameLongDataComposer = 5002;
    public const uint Game2StartCounterComposer = 5003;
    public const uint Game2UserJoinedGameComposer = 5004;
    public const uint Game2UserLeftGameComposer = 3665;
    public const uint Game2StopCounterComposer = 2747;
    public const uint Game2GameStartedComposer = 5009;
    public const uint Game2EnterArenaComposer = 5011;
    public const uint Game2ArenaEnteredComposer = 5013;
    public const uint Game2EnterArenaFailedComposer = 5014;
    public const uint Game2GameStatusComposer = 5015;
    public const uint Game2FullGameStatusComposer = 5016;
    public const uint Game2StageStartingComposer = 5017;
    public const uint Game2StageLoadComposer = 5018;
    public const uint Game2RejoinPreviousRoomComposer = 5019;
    public const uint Game2StageStillLoadingComposer = 5020;
    public const uint Game2GameEndingComposer = 5022;
    public const uint Game2GameChatComposer = 5023;
    public const uint Game2StageRunningComposer = 5024;
    public const uint Game2StageEndingComposer = 5025;
    public const uint Game2PlayerExitedGameArenaComposer = 5027;
    public const uint Game2PlayerRematchesComposer = 5029;
    public const uint Game2JoiningGameFailedComposer = 1654;
    public const uint Game2StartingGameFailedComposer = 113;
    public const uint Game2GameCancelledComposer = 2537;
    public const uint Game2GameNotFoundComposer = 65467;
    public const uint Game2UserBlockedComposer = 125;
    public const uint Game2TotalLeaderboardComposer = 1770;
    public const uint Game2FriendsLeaderboardComposer = 3697;
    public const uint Game2WeeklyFriendsLeaderboardComposer = 506;
    public const uint Game2TotalGroupLeaderboardComposer = 2441;
    public const uint Game2WeeklyGroupLeaderboardComposer = 2348;
    public const uint SnowWarGameTokensComposer = 3062;
    public const uint CraftableProductsComposer = 949;
    public const uint CraftingRecipeComposer = 380;
    public const uint CraftingResultComposer = 3544;
    public const uint CraftingRecipesAvailableComposer = 1491;
    // Room music
    public const uint JukeboxPlaylistComposer = 1623;
    public const uint JukeboxPlaylistFullComposer = 1496;
    public const uint NowPlayingComposer = 2050;
    public const uint OfficialSongIdComposer = 2333;
    public const uint SoundMachinePlaylistComposer = 1766;
    public const uint SongDisksInventoryComposer = 3116;
    public const uint QuizDataComposer = 3102;
    public const uint QuizResultsComposer = 2499;
    public const uint CampaignCalendarDataComposer = 3304;
}
