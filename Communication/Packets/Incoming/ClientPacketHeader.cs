namespace Plus.Communication.Packets.Incoming;

public static class ClientPacketHeader
{
    // Handshake
    public const uint InitDiffieHandshakeEvent = 3668;
    public const uint CompleteDiffieHandshakeEvent = 2896;
    public const uint UniqueIdEvent = 961;
    public const uint SSOTicketEvent = 3841;
    public const uint InfoRetrieveEvent = 0;

    // Avatar
    public const uint CheckUserNameEvent = 3949;
    public const uint ChangeUserNameEvent = 295;
    public const uint GetWardrobeEvent = 1958;
    public const uint SaveWardrobeOutfitEvent = 3456;

    //Preferences
    public const uint SetChatStylePreferenceEvent = 1471;
    public const uint SetChatPreferenceEvent = 65476;

    // Catalog
    public const uint GetCatalogIndexWithDiscountEvent = 3226; //1294
    public const uint GetCatalogPageEvent = 2291;
    public const uint GetProductOfferEvent = 3582;
    public const uint GetKickbackInfoEvent = 2179;
    public const uint GetClubGiftInfoEvent = 890;
    public const uint PurchaseFromCatalogEvent = 1191;
    public const uint PurchaseFromCatalogAsGiftEvent = 1372;
    public const uint GetBundleDiscountRulesetEvent = 2697;

    // Polls
    public const uint PollStartEvent = 484;
    public const uint PollRejectEvent = 129;
    public const uint PollAnswerEvent = 1449;

    // Navigator

    // Messenger
    public const uint GetFriendRequestsEvent = 2152;

    // Quests
    public const uint GetQuestListEvent = 176;
    public const uint StartQuestEvent = 2843;
    public const uint GetCurrentQuestEvent = 2208;
    public const uint CancelQuestEvent = 88;

    // Room Avatar
    public const uint ActionEvent = 3963;
    public const uint ApplySignEvent = 3977;
    public const uint DanceEvent = 2692;
    public const uint SitEvent = 1468;
    public const uint ChangeMottoEvent = 1845;
    public const uint LookToEvent = 1018;
    public const uint DropHandItemEvent = 2504;

    // Room Connection
    public const uint OpenFlatConnectionEvent = 2028;
    public const uint GoToFlatEvent = 65442;

    // Room Chat
    public const uint ChatEvent = 704;
    public const uint ShoutEvent = 1369;
    public const uint WhisperEvent = 1240;

    // Room Engine

    // Room Settings

    // Room Action

    // Users
    public const uint GetIgnoredUsersEvent = 638;

    // Moderation
    public const uint OpenHelpToolEvent = 1866;
    public const uint CallForHelpPendingCallsDeletedEvent = 2212;
    public const uint ModeratorActionEvent = 320;
    public const uint ModerationMsgEvent = 2956;
    public const uint ModerationMuteEvent = 2532;
    public const uint ModerationTradeLockEvent = 1878;
    public const uint GetModeratorUserRoomVisitsEvent = 1114;
    public const uint ModerationKickEvent = 3120;
    public const uint GetModeratorRoomInfoEvent = 452;
    public const uint GetModeratorUserInfoEvent = 2200;
    public const uint GetModeratorRoomChatlogEvent = 304;
    public const uint ModerateRoomEvent = 1577;
    public const uint GetModeratorUserChatlogEvent = 3800;
    public const uint GetModeratorTicketChatlogsEvent = 3214;
    public const uint ModerationCautionEvent = 545;
    public const uint ModerationBanEvent = 1388;
    public const uint SubmitNewTicketEvent = 137;
    public const uint CloseIssueDefaultActionEvent = 1127;

    // Inventory
    public const uint GetCreditsInfoEvent = 1445;
    public const uint GetAchievementsEvent = 3052;
    public const uint GetBadgesEvent = 3365;
    public const uint RequestFurniInventoryEvent = 522;
    public const uint SetActivatedBadgesEvent = 3491;
    public const uint AvatarEffectActivatedEvent = 1848;
    public const uint AvatarEffectSelectedEvent = 1645;

    public const uint InitTradeEvent = 2130;
    public const uint TradingCancelConfirmEvent = 2893;
    public const uint TradingModifyEvent = 1969;
    public const uint TradingOfferItemEvent = 3286;
    public const uint TradingCancelEvent = 761;
    public const uint TradingConfirmEvent = 3436;
    public const uint TradingOfferItemsEvent = 3882;
    public const uint TradingRemoveItemEvent = 2306;
    public const uint TradingAcceptEvent = 3266;

    // Register
    public const uint UpdateFigureDataEvent = 3669;

    // Groups
    public const uint GetBadgeEditorPartsEvent = 435;
    public const uint GetGroupCreationWindowEvent = 3533;
    public const uint GetGroupFurniSettingsEvent = 1623;
    public const uint DeclineGroupMembershipEvent = 2044;
    public const uint JoinGroupEvent = 2103;
    public const uint UpdateGroupColoursEvent = 235;
    public const uint SetGroupFavouriteEvent = 3725;
    public const uint GetGroupMembersEvent = 1756;

    // Group Forums
    public const uint PostGroupContentEvent = 17;
    public const uint GetForumStatsEvent = 2827;
    public const uint GetForumThreadEvent = 2068;
    public const uint UpdateForumReadMarkersEvent = 2038;
    public const uint GetForumsUnreadCountEvent = 1402;

    // Sound


    // Ambassador

    public const uint AmbassadorAlertEvent = 3881;

    public const uint RemoveMyRightsEvent = 1045;
    public const uint GiveHandItemEvent = 212;
    public const uint GoToHotelViewEvent = 2513;
    public const uint GetRoomFilterListEvent = 166;
    public const uint GetPromoArticlesEvent = 3763;
    public const uint ModifyWhoCanRideHorseEvent = 2946;
    public const uint RemoveFriendEvent = 486;
    public const uint RefreshCampaignEvent = 1289;
    public const uint AcceptFriendEvent = 1014;
    public const uint YouTubeVideoInformationEvent = 3789;
    public const uint FollowFriendEvent = 2864;
    public const uint SaveBotActionEvent = 3027;
    public const uint LetUserInEvent = 3764;
    public const uint GetMarketplaceItemStatsEvent = 994;
    public const uint GetSellablePetPalettesEvent = 3228;
    public const uint OpenCampaignCalendarDoorAsStaffEvent = 3124;
    public const uint SetUIFlagsEvent = 2630;
    public const uint DeleteRoomEvent = 189;
    public const uint SetSoundSettingsEvent = 2127;
    public const uint InitializeGameCenterEvent = 65468;
    public const uint RedeemOfferCreditsEvent = 1229;
    public const uint FriendListUpdateEvent = 217;
    public const uint FriendFurniConfirmLockEvent = 1051;
    public const uint UseHabboWheelEvent = 2257;
    public const uint SaveRoomSettingsEvent = 437;
    public const uint ToggleMoodlightEvent = 799;
    public const uint GetDailyQuestEvent = 3310;
    public const uint SetMannequinNameEvent = 1941;
    public const uint OneWayGateEvent = 2562;
    public const uint EventTrackerEvent = 65429;
    public const uint GetOccupiedTilesEvent = 1282;
    public const uint PickUpPetEvent = 3986;
    public const uint GetPetInventoryEvent = 133;
    public const uint GetRoomEntryTileEvent = 3355;
    public const uint GetOwnOffersEvent = 3217;
    public const uint CheckPetNameEvent = 1972;
    public const uint SetUserFocusPreferenceEvent = 1378;
    public const uint SubmitBullyReportEvent = 1390;
    public const uint RemoveRightsEvent = 3032;
    public const uint MakeOfferEvent = 3676;
    public const uint KickUserEvent = 2201;
    public const uint GetRoomSettingsEvent = 3301;
    public const uint GetThreadsListDataEvent = 684;
    public const uint GetForumUserProfileEvent = 2576;
    public const uint OpenWiredEvent = 3540;
    public const uint WiredMonitorRequestEvent = 10021;
    public const uint WiredRoomLogsPageEvent = 65422;
    public const uint WiredRoomSettingsRequestEvent = 10022;
    public const uint WiredRoomSettingsSaveEvent = 10023;
    public const uint WiredMenuPermissionsSaveEvent = 65465;
    public const uint ClickFurniEvent = 843;
    public const uint ClickUserEvent = 10020;
    public const uint WiredUserVariablesRequestEvent = 10024;
    public const uint WiredUserVariableUpdateEvent = 10025;
    public const uint WiredUserVariableManageEvent = 10026;
    public const uint WiredVariableHoldersPageEvent = 65307;
    public const uint WiredVariableHoldersRequestEvent = 65441;
    public const uint WiredVariableHashesEvent = 3698;
    public const uint WiredAllVariablesRequestEvent = 1327;
    public const uint SaveWiredEffectConfigEvent = 2554;
    public const uint SaveWiredSelectorConfigEvent = 268;
    public const uint SaveWiredAddonConfigEvent = 1692;
    public const uint SaveWiredVariableConfigEvent = 2836;
    public const uint GetRoomEntryDataEvent = 65460;
    public const uint JoinQueueEvent = 65471;
    public const uint CanCreateRoomEvent = 1817;
    public const uint SetTonerEvent = 3204;
    public const uint SaveWiredTriggerConfigEvent = 2786;
    public const uint PlaceBotEvent = 2612;
    public const uint GetRelationshipsEvent = 3435;
    public const uint SetMessengerInviteStatusEvent = 682;
    public const uint UseFurnitureEvent = 2401;
    public const uint GetUserFlatCatsEvent = 1816;
    public const uint AssignRightsEvent = 2084;
    public const uint GetRoomBannedUsersEvent = 933;
    public const uint ReleaseTicketEvent = 830;
    public const uint OpenPlayerProfileEvent = 1622;
    public const uint GetSanctionStatusEvent = 65447;
    public const uint CreditFurniRedeemEvent = 1583;
    public const uint DisconnectEvent = 1095;
    public const uint PickupObjectEvent = 1829;
    public const uint FindRandomFriendingRoomEvent = 3575;
    public const uint UseSellableClothingEvent = 1437;
    public const uint MoveObjectEvent = 3267;
    public const uint GetFurnitureAliasesEvent = 2273;
    public const uint TakeAdminRightsEvent = 538;
    public const uint ModifyRoomFilterListEvent = 2054;
    public const uint MoodlightUpdateEvent = 604;
    public const uint GetPetTrainingPanelEvent = 2384;
    public const uint GetSongInfoEvent = 2014;
    public const uint UseWallItemEvent = 282;
    public const uint GetTalentTrackEvent = 2916;
    public const uint GiveAdminRightsEvent = 564;
    public const uint GetCatalogIndexEvent = 1090;
    public const uint SendBullyReportEvent = 1714;
    public const uint CancelOfferEvent = 802;
    public const uint SaveWiredConditionConfigEvent = 3636;
    public const uint RedeemVoucherEvent = 3401;
    public const uint ThrowDiceEvent = 3291;
    public const uint CraftSecretEvent = 383;
    public const uint GetGameListEvent = 65391;
    public const uint SetRelationshipEvent = 2733;
    public const uint RequestFriendEvent = 206;
    public const uint MemoryPerformanceEvent = 285;
    public const uint ToggleYouTubeVideoEvent = 2546;
    public const uint SetMannequinFigureEvent = 1467;
    public const uint GetEventCategoriesEvent = 945;
    public const uint DeleteGroupThreadEvent = 1144;
    public const uint PurchaseGroupEvent = 1118;
    public const uint MessengerInitEvent = 1185;
    public const uint CancelTypingEvent = 574;
    public const uint GetMoodlightConfigEvent = 625;
    public const uint GetGroupInfoEvent = 1130;
    public const uint CreateFlatEvent = 1342;
    public const uint LatencyPingRequestEvent = 10;
    public const uint GetSelectedBadgesEvent = 1273;
    public const uint AddStickyNoteEvent = 2583;
    public const uint RideHorseEvent = 883;
    public const uint InitializeNewNavigatorEvent = 3683;
    public const uint GetForumsListDataEvent = 1206;
    public const uint ToggleMuteToolEvent = 3208;
    public const uint UpdateGroupIdentityEvent = 3335;
    public const uint UpdateStickyNoteEvent = 1602;
    public const uint UnbanUserFromRoomEvent = 70;
    public const uint UnignoreUserEvent = 1071;
    public const uint OpenGiftEvent = 345;
    public const uint ApplyDecorationEvent = 2144;
    public const uint GetRecipeConfigEvent = 2428; //3654
    public const uint ScrGetUserInfoEvent = 477;
    public const uint RemoveGroupMemberEvent = 1397;
    public const uint ConfirmRemoveGroupMemberEvent = 2359;
    public const uint DiceOffEvent = 1535;
    public const uint YouTubeGetNextVideo = 3201;
    public const uint RemoveFavouriteRoomEvent = 2035;
    public const uint RespectUserEvent = 2858;
    public const uint AddFavouriteRoomEvent = 3462;
    public const uint DeclineFriendEvent = 2064;
    public const uint StartTypingEvent = 496;
    public const uint GetGroupFurniConfigEvent = 384;
    public const uint SendRoomInviteEvent = 286;
    public const uint RemoveAllRightsEvent = 1138;
    public const uint GetYouTubeTelevisionEvent = 672;
    public const uint FindNewFriendsEvent = 1443;
    public const uint GetPromotableRoomsEvent = 1380;
    public const uint GetBotInventoryEvent = 3650;
    public const uint GetRentableSpaceEvent = 3823;
    public const uint OpenBotActionEvent = 3494;
    public const uint OpenCampaignCalendarDoorEvent = 32;
    public const uint DeleteGroupPostEvent = 586;
    public const uint UpdateGroupBadgeEvent = 263;
    public const uint PlaceObjectEvent = 3429;
    public const uint RemoveGroupFavouriteEvent = 2776;
    public const uint UpdateNavigatorSettingsEvent = 633;
    public const uint CheckGnomeNameEvent = 2812;
    public const uint NavigatorSearchEvent = 793;
    public const uint GetPetInformationEvent = 2471;
    public const uint GetGuestRoomEvent = 1427;
    public const uint UpdateThreadEvent = 1744;
    public const uint AcceptGroupMembershipEvent = 1902;
    public const uint GetMarketplaceConfigurationEvent = 2848;
    public const uint Game2GetWeeklyLeaderboardEvent = 1281;
    public const uint BuyOfferEvent = 3948;
    public const uint RemoveSaddleFromHorseEvent = 723;
    public const uint GiveRoomScoreEvent = 474;
    public const uint GetHabboClubWindowEvent = 985;
    public const uint DeleteStickyNoteEvent = 1248;
    public const uint MuteUserEvent = 99;
    public const uint ApplyHorseEffectEvent = 657;
    public const uint ClientHelloEvent = 4000; //4000
    public const uint OnBullyClickEvent = 65455;
    public const uint HabboSearchEvent = 2486;
    public const uint PickTicketEvent = 2262;
    public const uint GetGiftWrappingConfigurationEvent = 400;
    public const uint GetCraftingRecipesAvailableEvent = 3746;
    public const uint GetThreadDataEvent = 3086;
    public const uint ManageGroupEvent = 1959;
    public const uint PlacePetEvent = 2233;
    public const uint EditRoomPromotionEvent = 2586;
    public const uint UpdateFloorPropertiesEvent = 352;
    public const uint MoveWallItemEvent = 2629;
    public const uint VersionCheckEvent = 2650;
    public const uint PongEvent = 2281;
    public const uint DeleteGroupEvent = 319;
    public const uint UpdateGroupSettingsEvent = 1719;
    public const uint GetRecyclerPrizesEvent = 3404;
    public const uint PurchaseRoomAdEvent = 1042;
    public const uint PickUpBotEvent = 2638;
    public const uint GetOffersEvent = 324;
    public const uint GetHabboGroupBadgesEvent = 2784;
    public const uint GetUserTagsEvent = 65382;
    public const uint GetGameAchievementsEvent = 65475;
    public const uint GetCatalogRoomPromotionEvent = 2453;
    public const uint MoveAvatarEvent = 2166;
    public const uint SaveBrandingItemEvent = 1532;
    public const uint SaveEnforcedCategorySettingsEvent = 683;
    public const uint RespectPetEvent = 2136;
    public const uint GetMarketplaceCanMakeOfferEvent = 2865;
    public const uint UpdateMagicTileAdjacentEvent = 65449;
    public const uint UpdateMagicTileEvent = 1823;
    public const uint GetStickyNoteEvent = 2220;
    public const uint IgnoreUserEvent = 3743;
    public const uint BanUserEvent = 1742;
    public const uint UpdateForumSettingsEvent = 1344;
    public const uint GetRoomRightsEvent = 959;
    public const uint SendMsgEvent = 1521;
    public const uint CloseTicketEvent = 415;

    //NotImplemented

    //Camera
    //public const uint InitCameraEvent =;
    //public const uint PhotoCompetitionEvent =;
    //public const uint PublishPhotoEvent =;
    //public const uint PurchasePhotoEvent =;
    //public const uint RenderRoomEvent =;
    //public const uint RenderRoomThumbnailEvent =;

    //campaign
    //public const uint OpenCampaignCalendarDoorAsStaffEvent =;
    //public const uint OpenCampaignCalendarDoorEvent =;

    //catalog
    //public const uint GetBonusRareInfoEvent =;
    //public const uint GetCatalogPageExpirationEvent =;
    //public const uint GetCatalogPageWithEarliestExpiryEvent =;
    //public const uint GetDirectClubBuyAvailableEvent =;
    //public const uint GetHabboBasicMembershipExtendOfferEvent =;
    //public const uint GetLimitedOfferAppearingNextEvent =;
    public const uint GetHabboClubExtendOfferEvent = 3090;
    //public const uint GetIsOfferGiftableEvent =;
    //public const uint GetNextTargetedOfferEvent =;
    //public const uint GetSeasonalCalendarDailyOfferEvent =;
    //public const uint MarkCatalogNewAdditionsPageOpenedEvent =;
    public const uint PurchaseBasicMembershipExtensionEvent = 1458;
    //public const uint PurchaseTargetedOfferEvent =;
    public const uint PurchaseVipMembershipExtensionEvent = 2339;
    public const uint SelectClubGiftEvent = 1872;
    //public const uint SetTargetedOfferStateEvent =;
    //public const uint ShopTargetedOfferViewedEvent =;


    //recycler
    public const uint GetRecyclerStatusEvent = 2647;
    public const uint RecyclerRecycleEvent = 3029;


    //FriendList
    //public const uint FriendFurniConfirmLockEvent =;
    //public const uint SetRelationshipStatusEvent =;
    //public const uint VisitUserEvent =;

    //competition
    //public const uint ForwardToACompetitionRoomEvent =;
    //public const uint ForwardToASubmittableRoomEvent =;
    //public const uint ForwardToRandomCompetitionRoomEvent =;
    //public const uint GetCurrentTimingCodeEvent =;
    //public const uint GetIsUserPartOfCompetitionEvent =;
    //public const uint GetSecondsUntilEvent =;
    //public const uint RoomCompetitionInitEvent =;
    //public const uint SubmitRoomToCompetitionEvent =;
    //public const uint VoteForRoomEvent =;

    //crafting
    public const uint CraftEvent = 2324;
    //public const uint CraftSecretEvent =;
    public const uint GetCraftableProductsEvent = 2698;
    public const uint GetCraftingRecipeEvent = 1420;
    //public const uint GetCraftingRecipesAvailableEvent =;

    // SnowStorm (AIR Game2 payloads); internal IDs match Octane's wire IDs.
    public const uint Game2CheckGameDirectoryStatusEvent = 1199;
    public const uint Game2GetAccountGameStatusEvent = 3299;
    public const uint Game2QuickJoinEvent = 6012;
    public const uint Game2LeaveLobbyEvent = 6013;
    public const uint Game2LoadStageReadyEvent = 1438;
    public const uint Game2ExitGameEvent = 2749;
    public const uint Game2GameChatEvent = 3605;
    public const uint Game2PlayAgainEvent = 2645;
    public const uint Game2SetUserMoveTargetEvent = 6003;
    public const uint Game2ThrowSnowballAtPositionEvent = 6004;
    public const uint Game2ThrowSnowballAtHumanEvent = 6005;
    public const uint Game2MakeSnowballEvent = 6006;
    public const uint Game2RequestFullStatusUpdateEvent = 1980;
    public const uint Game2GetTotalLeaderboardEvent = 6027;
    public const uint Game2GetFriendsLeaderboardEvent = 6028;
    public const uint Game2GetWeeklyFriendsLeaderboardEvent = 3016;
    public const uint Game2GetTotalGroupLeaderboardEvent = 3229;
    public const uint Game2GetWeeklyGroupLeaderboardEvent = 1572;
    public const uint GetSnowWarGameTokensOfferEvent = 402;
    public const uint PurchaseSnowWarGameTokensOfferEvent = 3989;




    public const uint TriggerHabbiconEvent = 2172;
    public const uint GetHabbiconShopDataEvent = 3937;
    public const uint GetHabbiconInfoEvent = 3333;
    public const uint BuyHabbiconEvent = 279;
    public const uint BuyHabbiconCollectionEvent = 1209;
    public const uint ClaimHabbiconEvent = 1068;
    public const uint FavoriteHabbiconEvent = 125;
    public const uint UnfavoriteHabbiconEvent = 3255;
    public const uint UnseenResetCategoryEvent = 65428;
    public const uint UnseenResetItemsEvent = 65459;
    public const uint SendMessengerMessageEvent = 4902;
    // Camera internal IDs; wire IDs are revision-specific.
    public const uint InitCameraEvent = 706;
    public const uint PhotoCompetitionEvent = 3200;
    public const uint PublishPhotoEvent = 1021;
    public const uint PurchasePhotoEvent = 2218;
    public const uint RenderRoomEvent = 3044;
    public const uint RenderRoomThumbnailEvent = 15;
    public const uint GetRewardTracksEvent = 9450;
    public const uint ClaimRewardTrackPrizeEvent = 3859;
    public const uint PurchaseRewardTrackPremiumEvent = 1663;
    // Octane catalog editor and furni editor; the same ids on the wire.
    public const uint FurniEditorSearchEvent = 10040;
    public const uint FurniEditorDetailEvent = 10041;
    public const uint FurniEditorBySpriteEvent = 10042;
    public const uint FurniEditorInteractionsEvent = 10043;
    public const uint FurniEditorUpdateEvent = 10044;
    public const uint FurniEditorDeleteEvent = 10045;
    public const uint FurniEditorUpdateFurnidataEvent = 10046;
    public const uint FurniEditorRevertFurnidataEvent = 10048;
    public const uint FurniEditorImportTextEvent = 10049;
    public const uint CatalogAdminSavePageEvent = 10050;
    public const uint CatalogAdminCreatePageEvent = 10051;
    public const uint CatalogAdminDeletePageEvent = 10052;
    public const uint CatalogAdminSaveOfferEvent = 10053;
    public const uint CatalogAdminCreateOfferEvent = 10054;
    public const uint CatalogAdminDeleteOfferEvent = 10055;
    public const uint CatalogAdminMoveOfferEvent = 10056;
    public const uint CatalogAdminMovePageEvent = 10057;
    public const uint CatalogAdminPublishEvent = 10058;
    public const uint CatalogAdminSavePageImagesEvent = 10060;
    public const uint CatalogAdminSavePageIconEvent = 10061;
    public const uint CatalogAdminLoadOfferEvent = 10062;
    public const uint CatalogAdminLoadPageEvent = 10063;
    public const uint CatalogAdminSetPageEnabledEvent = 10064;
    public const uint CatalogAdminSetPageVisibleEvent = 10065;
    public const uint CatalogAdminReorderOffersEvent = 10066;
    public const uint CatalogStudioOpenSessionEvent = 10067;
    public const uint CatalogStudioLoadHistoryEvent = 10071;
    public const uint CatalogStudioUndoEvent = 10072;
    // Housekeeping (in-client admin panel); internal IDs match Octane's wire IDs.
    public const uint HousekeepingFindUserByNameEvent = 9100;
    public const uint HousekeepingFindUserByIdEvent = 9101;
    public const uint HousekeepingBanUserEvent = 9102;
    public const uint HousekeepingUnbanUserEvent = 9103;
    public const uint HousekeepingMuteUserEvent = 9104;
    public const uint HousekeepingKickUserEvent = 9105;
    public const uint HousekeepingForceDisconnectUserEvent = 9106;
    public const uint HousekeepingSetUserRankEvent = 9107;
    public const uint HousekeepingTradeLockUserEvent = 9108;
    public const uint HousekeepingResetUserPasswordEvent = 9109;
    public const uint HousekeepingFindRoomByIdEvent = 9110;
    public const uint HousekeepingSearchRoomsEvent = 9111;
    public const uint HousekeepingRoomStateEvent = 9112;
    public const uint HousekeepingMuteRoomEvent = 9113;
    public const uint HousekeepingKickAllFromRoomEvent = 9114;
    public const uint HousekeepingTransferRoomOwnershipEvent = 9115;
    public const uint HousekeepingDeleteRoomEvent = 9116;
    public const uint HousekeepingGiveCreditsEvent = 9117;
    public const uint HousekeepingGiveCurrencyEvent = 9118;
    public const uint HousekeepingGrantItemEvent = 9119;
    public const uint HousekeepingSetHcSubscriptionEvent = 9120;
    public const uint HousekeepingSendHotelAlertEvent = 9121;
    public const uint HousekeepingGetDashboardEvent = 9122;
    public const uint HousekeepingListActionLogEvent = 9123;
    public const uint HousekeepingGetRolesEvent = 9130;
    public const uint HousekeepingGetRoleMembersEvent = 9131;
    public const uint HousekeepingGetUserOverridesEvent = 9132;
    public const uint HousekeepingGetRolesAuditEvent = 9133;
    public const uint HousekeepingSaveRoleEvent = 9134;
    public const uint HousekeepingDeleteRoleEvent = 9135;
    public const uint HousekeepingSetRolePermissionEvent = 9136;
    public const uint HousekeepingSetRoleLimitEvent = 9137;
    public const uint HousekeepingAssignRoleEvent = 9138;
    public const uint HousekeepingRevokeRoleEvent = 9139;
    public const uint HousekeepingSetUserOverrideEvent = 9140;
    public const uint HousekeepingRemoveUserOverrideEvent = 9141;
    public const uint GetQuizQuestionsEvent = 3537;
    public const uint PostQuizAnswersEvent = 1837;
    // Room music
    public const uint AddJukeboxDiskEvent = 3975;
    public const uint RemoveJukeboxDiskEvent = 577;
    public const uint GetJukeboxPlaylistEvent = 528;
    public const uint GetSoundMachinePlaylistEvent = 3109;
    public const uint GetUserSongDisksEvent = 2411;
    public const uint GetNowPlayingEvent = 949;
    public const uint GetOfficialSongIdEvent = 2774;
    public const uint WiredVariableInspectionRequestEvent = 10112;
    public const uint WiredUserVariableUpdate64Event = 10110;
    public const uint WiredUserVariableManage64Event = 10111;
    public const uint WiredUserVariablesRequest64Event = 10113;
    public const uint WiredVariableHoldersRequest64Event = 10114;
    public const uint WiredVariableHoldersPage64Event = 10115;
    public const uint ChestOpenEvent = 9327;
    public const uint ChestCloseEvent = 9339;
    public const uint ChestStartDepositEvent = 9324;
    public const uint ChestDepositInventoryItemEvent = 9325;
    public const uint ChestWithdrawAllEvent = 9326;
    public const uint ChestWithdrawFurniEvent = 9320;
    public const uint ChestWithdrawCoinsEvent = 9314;
    public const uint ChestDepositCoinsEvent = 9313;
    public const uint ChestSaveOptionsEvent = 9338;
    public const uint ChestSavePreferencesEvent = 9315;
    public const uint ChestEnableWiredEvent = 9345;
    public const uint ChestSaveNotificationsEvent = 9316;
    public const uint WiredChestOfferItemsEvent = 9335;
    public const uint WiredChestAcceptEvent = 9336;
    public const uint WiredChestCancelEvent = 9337;
    public const uint ChestUpgradeEvent = 9317;
    public const uint WiredChestLockEvent = 9329;
}
