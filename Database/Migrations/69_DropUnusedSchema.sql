-- Drops tables and columns that neither the emulator nor the CMS reads or writes. Apply while PlusEMU is stopped.
-- Write-only audit tables (logs_client_*, chatlogs_console*, ambassador_logs, catalog_admin_log, furni_editor_log)
-- stay: they are records for staff, not stale data.

-- Clothing furni carry their figure sets in furniture.custom_params; tickets live in memory; the client reads its
-- texts and talents from gamedata; nothing reads server_status.
DROP TABLE IF EXISTS `catalog_clothing`;
DROP TABLE IF EXISTS `moderation_tickets`;
DROP TABLE IF EXISTS `achievements_talents`;
DROP TABLE IF EXISTS `client_external_badge_texts`;
DROP TABLE IF EXISTS `client_external_texts`;
DROP TABLE IF EXISTS `server_status`;

ALTER TABLE `furniture` DROP COLUMN IF EXISTS `clothing_id`;
ALTER TABLE `bans` DROP COLUMN IF EXISTS `appeal_state`;
ALTER TABLE `bots`
    DROP COLUMN IF EXISTS `min_x`, DROP COLUMN IF EXISTS `min_y`, DROP COLUMN IF EXISTS `max_x`, DROP COLUMN IF EXISTS `max_y`,
    DROP COLUMN IF EXISTS `effect`, DROP COLUMN IF EXISTS `dance`;
ALTER TABLE `bots_speech` DROP COLUMN IF EXISTS `shout`, DROP COLUMN IF EXISTS `type`;
ALTER TABLE `bots_pet_commands` DROP COLUMN IF EXISTS `input_title`;
ALTER TABLE `catalog_promotions` DROP COLUMN IF EXISTS `unknown`, DROP COLUMN IF EXISTS `parent_id`;
ALTER TABLE `games_config` DROP COLUMN IF EXISTS `socket_policy_port`, DROP COLUMN IF EXISTS `last_reset`;
ALTER TABLE `room_models` DROP COLUMN IF EXISTS `poolmap`;
ALTER TABLE `server_settings` DROP COLUMN IF EXISTS `description`;
ALTER TABLE `user_info` DROP COLUMN IF EXISTS `reg_timestamp`, DROP COLUMN IF EXISTS `login_timestamp`;
ALTER TABLE `user_statistics`
    DROP COLUMN IF EXISTS `lev_builder`, DROP COLUMN IF EXISTS `lev_social`, DROP COLUMN IF EXISTS `lev_identity`,
    DROP COLUMN IF EXISTS `lev_explore`, DROP COLUMN IF EXISTS `tickets_answered`;
-- Trading locks live in user_info.trading_locked; machine ids are only logged in logs_client_staff.
ALTER TABLE `users` DROP INDEX IF EXISTS `machine_id`, DROP COLUMN IF EXISTS `machine_id`, DROP COLUMN IF EXISTS `trading_locked`;
ALTER TABLE `users_settings`
    DROP COLUMN IF EXISTS `is_muted`, DROP COLUMN IF EXISTS `hide_online`, DROP COLUMN IF EXISTS `hide_inroom`,
    DROP COLUMN IF EXISTS `advertising_report_blocked`;
ALTER TABLE `wordfilter` DROP COLUMN IF EXISTS `addedby`;
