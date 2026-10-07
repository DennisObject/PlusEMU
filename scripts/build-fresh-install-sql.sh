#!/usr/bin/env bash
# Rebuild Database/FreshInstall.sql from a MariaDB database that already has
# every Resources/SQLs/Updates and Database/Migrations file applied.
# Schema comes from every table; rows only from the game-content tables below,
# so no users, rooms, items or logs end up in the file.
# Usage: scripts/build-fresh-install-sql.sh <mariadb container> <database>
set -euo pipefail
CONTAINER=${1:?mariadb container}
DB=${2:?database}
OUT="$(dirname "$0")/../Database/FreshInstall.sql"

CONTENT_TABLES="achievements achievements_talents acl_permissions badge_definitions
bots_pet_commands bots_pet_responses bots_responses catalog_bot_presets catalog_clothing
catalog_club_offers catalog_deals catalog_items catalog_pages catalog_pet_races
catalog_promotions client_external_badge_texts client_external_texts club_gift_offers
furniture games_config groups_items habbicons habbicon_collections moderation_presets
moderation_preset_action_categories moderation_preset_action_messages moderation_topics
moderation_topic_actions navigator_categories quests reward_tracks reward_track_prizes
reward_track_tasks reward_track_task_levels roles role_limits role_permissions
room_chat_styles room_models server_landing server_locale server_rewards server_settings
talents talents_sub_levels wordfilter"

dump() {
    # The first line of a MariaDB 11 dump enables sandbox mode, which older
    # clients reject, so it is dropped.
    docker exec "$CONTAINER" sh -c 'MYSQL_PWD="$MARIADB_ROOT_PASSWORD" exec mariadb-dump -uroot --skip-dump-date "$@"' sh "$@" |
        sed '/enable the sandbox mode/d'
}

{
    echo "-- PlusEMU fresh install: the full schema plus game content (catalog, furniture, roles, ...)."
    echo "-- Import into an empty database instead of Original Database.sql and the update scripts."
    echo "-- Regenerate with scripts/build-fresh-install-sql.sh after adding a migration."
    # MariaDB 10.6 (Ubuntu 22.04) has no uca1400 collations.
    dump --no-data --routines --triggers "$DB" |
        sed -E 's/ AUTO_INCREMENT=[0-9]+//; s/utf8mb4_uca1400_ai_ci/utf8mb4_unicode_ci/g'
    # shellcheck disable=SC2086
    dump --no-create-info --skip-triggers "$DB" $CONTENT_TABLES
    echo "INSERT INTO \`server_status\` (\`users_online\`, \`loaded_rooms\`) VALUES (0, 0);"
} > "$OUT"
echo "wrote $OUT ($(wc -c < "$OUT") bytes)"
