-- One character set and engine for the whole database: every table InnoDB, every text column utf8mb4 with
-- utf8mb4_unicode_ci (what the CMS creates its tables with), so names and chat keep emoji and any language, and string
-- joins between tables compare with one collation and can use their indexes. Columns that are deliberately ASCII or
-- binary (permission keys, idempotency keys, UUIDs, JSON payloads, case-sensitive codes) keep their collation.
-- Apply while PlusEMU is stopped; large tables are rebuilt. Each table's statement is built from its current columns,
-- and a table this install does not have is skipped. No stored procedures, so it runs through any MySQL client.

ALTER DATABASE CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- MariaDB will not change the collation of a column in a foreign key, so those keys come off and go back unchanged.
CREATE TEMPORARY TABLE `migration_65_keys` AS
    SELECT r.TABLE_NAME, r.CONSTRAINT_NAME,
           CONCAT('ADD CONSTRAINT `', r.CONSTRAINT_NAME, '` FOREIGN KEY (',
                  GROUP_CONCAT(CONCAT('`', k.COLUMN_NAME, '`') ORDER BY k.ORDINAL_POSITION), ') REFERENCES `', r.REFERENCED_TABLE_NAME, '` (',
                  GROUP_CONCAT(CONCAT('`', k.REFERENCED_COLUMN_NAME, '`') ORDER BY k.ORDINAL_POSITION), ') ON DELETE ', r.DELETE_RULE,
                  ' ON UPDATE ', r.UPDATE_RULE) AS `addition`
    FROM information_schema.REFERENTIAL_CONSTRAINTS r
    JOIN information_schema.KEY_COLUMN_USAGE k
      ON k.CONSTRAINT_SCHEMA = r.CONSTRAINT_SCHEMA AND k.TABLE_NAME = r.TABLE_NAME AND k.CONSTRAINT_NAME = r.CONSTRAINT_NAME
    LEFT JOIN information_schema.COLUMNS c
      ON c.TABLE_SCHEMA = k.TABLE_SCHEMA AND c.TABLE_NAME = k.TABLE_NAME AND c.COLUMN_NAME = k.COLUMN_NAME
    LEFT JOIN information_schema.COLUMNS p
      ON p.TABLE_SCHEMA = k.TABLE_SCHEMA AND p.TABLE_NAME = k.REFERENCED_TABLE_NAME AND p.COLUMN_NAME = k.REFERENCED_COLUMN_NAME
    WHERE r.CONSTRAINT_SCHEMA = DATABASE()
    GROUP BY r.TABLE_NAME, r.CONSTRAINT_NAME, r.REFERENCED_TABLE_NAME, r.DELETE_RULE, r.UPDATE_RULE
    HAVING SUM(c.CHARACTER_SET_NAME IS NOT NULL AND c.CHARACTER_SET_NAME <> 'ascii' AND c.COLLATION_NAME NOT IN ('utf8mb4_unicode_ci', 'utf8mb4_bin') OR p.CHARACTER_SET_NAME IS NOT NULL AND p.CHARACTER_SET_NAME <> 'ascii' AND p.COLLATION_NAME NOT IN ('utf8mb4_unicode_ci', 'utf8mb4_bin')) > 0;

SET SESSION group_concat_max_len = 1048576;
PREPARE `migration_65_drop` FROM
    'SELECT COALESCE(CONCAT(''ALTER TABLE `'', MAX(TABLE_NAME), ''` '', GROUP_CONCAT(CONCAT(''DROP FOREIGN KEY `'', CONSTRAINT_NAME, ''`'') SEPARATOR '', '')), ''DO 0'')
     INTO @migration_65_statement FROM `migration_65_keys` WHERE TABLE_NAME = ?';
PREPARE `migration_65_restore` FROM
    'SELECT COALESCE(CONCAT(''ALTER TABLE `'', MAX(TABLE_NAME), ''` '', GROUP_CONCAT(`addition` SEPARATOR '', '')), ''DO 0'')
     INTO @migration_65_statement FROM `migration_65_keys` WHERE TABLE_NAME = ?';
PREPARE `migration_65_convert` FROM
    'SELECT COALESCE(MAX(CONCAT(''ALTER TABLE `'', t.TABLE_NAME, ''`'', IF(t.ENGINE <> ''InnoDB'', '' ENGINE=InnoDB,'', ''''),
            '' DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci'',
            COALESCE((SELECT GROUP_CONCAT(CONCAT('', MODIFY `'', c.COLUMN_NAME, ''` '', c.COLUMN_TYPE, '' CHARACTER SET utf8mb4 COLLATE '',
                         IF(c.COLLATION_NAME LIKE ''%\\_bin'', ''utf8mb4_bin'', ''utf8mb4_unicode_ci''),
                         IF(c.EXTRA LIKE ''%GENERATED%'',
                            CONCAT('' GENERATED ALWAYS AS ('', c.GENERATION_EXPRESSION, '') '', IF(c.EXTRA LIKE ''STORED%'', ''STORED'', ''VIRTUAL'')),
                            CONCAT(IF(c.IS_NULLABLE = ''YES'', '' NULL'', '' NOT NULL''),
                                   IF(c.COLUMN_DEFAULT IS NULL, '''', CONCAT('' DEFAULT '', c.COLUMN_DEFAULT)))),
                         IF(c.COLUMN_COMMENT = '''', '''', CONCAT('' COMMENT '', QUOTE(c.COLUMN_COMMENT))))
                         ORDER BY c.ORDINAL_POSITION SEPARATOR '''')
                      FROM information_schema.COLUMNS c
                      WHERE c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME AND c.CHARACTER_SET_NAME IS NOT NULL AND c.CHARACTER_SET_NAME <> ''ascii'' AND c.COLLATION_NAME NOT IN (''utf8mb4_unicode_ci'', ''utf8mb4_bin'')), ''''))), ''DO 0'')
     INTO @migration_65_statement FROM information_schema.TABLES t
     WHERE t.TABLE_SCHEMA = DATABASE() AND t.TABLE_TYPE = ''BASE TABLE'' AND t.TABLE_NAME = ?
       AND (t.ENGINE <> ''InnoDB'' OR t.TABLE_COLLATION <> ''utf8mb4_unicode_ci'' OR EXISTS (
            SELECT 1 FROM information_schema.COLUMNS c WHERE c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME AND c.CHARACTER_SET_NAME IS NOT NULL AND c.CHARACTER_SET_NAME <> ''ascii'' AND c.COLLATION_NAME NOT IN (''utf8mb4_unicode_ci'', ''utf8mb4_bin'')))';

SET @migration_65_checks = @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;
EXECUTE `migration_65_drop` USING 'catalog_offer_products'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_drop` USING 'catalog_pages'; EXECUTE IMMEDIATE @migration_65_statement;

EXECUTE `migration_65_convert` USING 'achievements'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'acl_audit_log'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'acl_permissions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'activity_log'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'ambassador_logs'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'badge_definitions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'bans'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'bots'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'bots_pet_commands'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'bots_pet_responses'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'bots_petdata'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'bots_responses'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'bots_speech'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'cache'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'cache_locks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'camera_accounts'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'camera_competition_entries'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'camera_media'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'camera_publications'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'camera_purchases'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'camera_quota'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'campaign_calendar_rewards'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'campaign_calendars'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_admin_log'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_bot_presets'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_club_offers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_marketplace_data'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_marketplace_offers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_offer_limited'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_offer_products'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_offers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_page_images'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_page_offers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_page_texts'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_pages'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_pet_races'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_promotions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'catalog_vouchers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'chatlogs'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'chatlogs_console'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'chatlogs_console_invitations'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'claimed_referral_logs'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'club_credit_spending'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'club_gift_claims'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'club_gift_offers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'club_membership_intervals'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'club_paydays'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'crafting_altars_recipes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'crafting_recipes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'crafting_recipes_ingredients'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'failed_jobs'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'furni_editor_log'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'furniture'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'games_config'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'group_forum_messages'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'group_forum_post_limits'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'group_forum_read_markers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'group_forum_threads'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'group_forums'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'group_memberships'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'group_requests'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'groups'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'groups_items'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'habbicon_collections'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'habbicons'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'home_categories'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'home_items'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'housekeeping_log'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'housekeeping_online_peaks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'items'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'items_groups'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'items_youtube'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'logs_client_namechange'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'logs_client_staff'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'logs_client_trade'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'messenger_friendships'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'messenger_offline_messages'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'messenger_requests'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'migrations'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'moderation_preset_action_categories'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'moderation_preset_action_messages'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'moderation_presets'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'moderation_topic_actions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'moderation_topics'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'navigator_categories'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'navigator_publics'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'personal_access_tokens'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'quests'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'rcon_grants'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'recycler_levels'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'recycler_prizes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'recycler_settings'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'referrals'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'reward_track_prizes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'reward_track_task_levels'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'reward_track_tasks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'reward_tracks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'role_limits'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'role_permissions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'roles'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_bans'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_chat_styles'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_filter'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_items_moodlight'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_items_tele_links'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_items_toner'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_models'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_music_disc_definitions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_music_players'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_music_playlist'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_music_songs'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_poll_questions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_poll_responses'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_polls'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_promotions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_rights'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'room_wired_settings'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'rooms'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'safety_quiz_questions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'safety_quizzes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'server_landing'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'server_locale'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'server_reward_logs'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'server_rewards'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'server_settings'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'sessions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'snowwar_game_tokens'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'snowwar_scores'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'snowwar_token_offers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'taggables'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'tags'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'talents'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'talents_sub_levels'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_access_tokens'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_achievements'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_badges'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_calendar_claims'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_clothing'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_club_memberships'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_crafting_recipes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_currencies'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_effects'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_favorites'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_home_items'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_home_messages'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_home_ratings'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_ignores'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_info'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_permissions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_presents'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_quests'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_recycler'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_referrals'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_remember_tokens'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_roles'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_roomvisits'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_safety_quizzes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_saved_searches'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_sessions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_statistics'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_talent_rewards'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_vouchers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'user_wardrobe'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'users'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'users_habbicons'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'users_reward_track_prizes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'users_reward_track_tasks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'users_reward_tracks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'users_settings'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_ads'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_api_idempotency_keys'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_article_comments'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_article_reactions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_articles'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_badge_grant_locks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_badges'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_beta_codes'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_drawbadges'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_help_center_categories'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_help_center_ticket_replies'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_help_center_tickets'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_housekeeping_permissions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_installation'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_ip_blacklist'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_ip_whitelist'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_languages'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_maintenance_tasks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_open_positions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_password_resets'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_paypal_transactions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_permissions'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_rare_value_categories'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_rare_values'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_registration_locks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_rule_categories'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_rules'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_settings'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_shop_article_features'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_shop_articles'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_shop_categories'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_shop_items'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_shop_package_items'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_shop_packages'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_shop_purchases'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_shop_vouchers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_staff_applications'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_teams'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_used_shop_vouchers'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_user_guestbooks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_users'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'website_wordfilter'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'wired_item_configurations'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'wired_items'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'wired_reward_state'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'wired_variable_locks'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'wired_variable_values'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_convert` USING 'wordfilter'; EXECUTE IMMEDIATE @migration_65_statement;

EXECUTE `migration_65_restore` USING 'catalog_offer_products'; EXECUTE IMMEDIATE @migration_65_statement;
EXECUTE `migration_65_restore` USING 'catalog_pages'; EXECUTE IMMEDIATE @migration_65_statement;
SET FOREIGN_KEY_CHECKS = @migration_65_checks;

DEALLOCATE PREPARE `migration_65_drop`;
DEALLOCATE PREPARE `migration_65_restore`;
DEALLOCATE PREPARE `migration_65_convert`;
DROP TEMPORARY TABLE `migration_65_keys`;
SET @migration_65_statement = NULL, @migration_65_checks = NULL;
