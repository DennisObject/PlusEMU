-- Apply while PlusEMU is stopped. Back up the database first; Plus does not auto-run SQL updates.
-- Creates the introduction reward track. Safe to run again: existing rows and player progress stay.

CREATE TABLE IF NOT EXISTS reward_tracks (
    id VARCHAR(64) NOT NULL,
    theme VARCHAR(64) NOT NULL DEFAULT 'blue',
    sort_order INT NOT NULL DEFAULT 0,
    starts_at INT NOT NULL DEFAULT 0,
    ends_at INT NOT NULL DEFAULT 0,
    has_premium TINYINT(1) NOT NULL DEFAULT 0,
    premium_task_points_boost DOUBLE NOT NULL DEFAULT 0,
    premium_instant_points INT NOT NULL DEFAULT 0,
    premium_cost_diamonds INT NOT NULL DEFAULT 0,
    premium_cost_credits INT NOT NULL DEFAULT 0,
    enabled TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS reward_track_tasks (
    track_id VARCHAR(64) NOT NULL,
    id VARCHAR(64) NOT NULL,
    action_type VARCHAR(64) NOT NULL,
    parameter VARCHAR(255) NOT NULL DEFAULT '',
    premium TINYINT(1) NOT NULL DEFAULT 0,
    sort_order INT NOT NULL DEFAULT 0,
    PRIMARY KEY (track_id, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS reward_track_task_levels (
    track_id VARCHAR(64) NOT NULL,
    task_id VARCHAR(64) NOT NULL,
    level INT NOT NULL,
    required_count INT NOT NULL DEFAULT 1,
    points_reward INT NOT NULL DEFAULT 0,
    premium TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (track_id, task_id, level)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS reward_track_prizes (
    track_id VARCHAR(64) NOT NULL,
    id VARCHAR(64) NOT NULL,
    required_points INT NOT NULL DEFAULT 0,
    product_item_type_id INT NOT NULL DEFAULT 0,
    reward_type VARCHAR(32) NOT NULL DEFAULT 'duckets',
    extra_params VARCHAR(255) NOT NULL DEFAULT '',
    reward_amount INT NOT NULL DEFAULT 0,
    premium TINYINT(1) NOT NULL DEFAULT 0,
    sort_order INT NOT NULL DEFAULT 0,
    PRIMARY KEY (track_id, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS users_reward_tracks (
    user_id INT NOT NULL,
    track_id VARCHAR(64) NOT NULL,
    points INT NOT NULL DEFAULT 0,
    premium TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, track_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS users_reward_track_tasks (
    user_id INT NOT NULL,
    track_id VARCHAR(64) NOT NULL,
    task_id VARCHAR(64) NOT NULL,
    progress_count INT NOT NULL DEFAULT 0,
    peak_count INT NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, track_id, task_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS users_reward_track_prizes (
    user_id INT NOT NULL,
    track_id VARCHAR(64) NOT NULL,
    prize_id VARCHAR(64) NOT NULL,
    claimed_at INT NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, track_id, prize_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @peak_exists = (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'users_reward_track_tasks'
      AND COLUMN_NAME = 'peak_count');
SET @peak_ddl = IF(@peak_exists = 0,
    'ALTER TABLE users_reward_track_tasks ADD COLUMN peak_count INT NOT NULL DEFAULT 0',
    'SELECT 1');
PREPARE peak_ddl FROM @peak_ddl;
EXECUTE peak_ddl;
DEALLOCATE PREPARE peak_ddl;
UPDATE users_reward_track_tasks SET peak_count = progress_count WHERE peak_count < progress_count;

INSERT IGNORE INTO reward_tracks
(id, theme, sort_order, starts_at, ends_at, has_premium, premium_task_points_boost,
 premium_instant_points, premium_cost_diamonds, premium_cost_credits, enabled)
VALUES ('introduction', 'blue', 0, 0, 0, 1, 1.5, 25, 0, 25, 1);

INSERT IGNORE INTO reward_track_tasks (track_id, id, action_type, parameter, premium, sort_order) VALUES
('introduction', 'chat_with_users', 'chat_with_someone', '', 0, 1),
('introduction', 'visit_rooms', 'enter_other_users_room', '', 0, 2),
('introduction', 'place_furniture', 'place_item', '', 0, 3),
('introduction', 'give_respect', 'give_respect', '', 0, 4),
('introduction', 'buy_catalog_furni', 'buy_from_catalogue', '', 0, 5),
('introduction', 'change_motto', 'change_motto', '', 0, 6),
('introduction', 'change_outfit', 'change_figure', '', 0, 7),
('introduction', 'close_love_lock', 'friend_furni_locked', '', 0, 8),
('introduction', 'create_room', 'create_room', '', 0, 9),
('introduction', 'dance_in_room', 'dance', '', 0, 10),
('introduction', 'feed_pet', 'pet_eat', '', 0, 11),
('introduction', 'follow_friend', 'follow_friend', '', 0, 12),
('introduction', 'go_swimming', 'swim', '', 0, 13),
('introduction', 'grab_drink', 'find_hand_item', '', 0, 14),
('introduction', 'level_pet', 'pet_level', '', 0, 15),
('introduction', 'make_friends', 'request_friend', '', 0, 16),
('introduction', 'move_furniture', 'move_item', '', 0, 17),
('introduction', 'pet_a_pet', 'pet_respect', '', 0, 18),
('introduction', 'place_builders_club_furni', 'place_builders_club_furni', '', 0, 19),
('introduction', 'publish_picture', 'publish_picture', '', 0, 20),
('introduction', 'replenish_respect', 'replenish_respect', '', 0, 21),
('introduction', 'rotate_furniture', 'rotate_item', '', 0, 22),
('introduction', 'send_messenger_invite', 'send_messenger_invite', '', 0, 23),
('introduction', 'send_messenger_message', 'send_messenger_message', '', 0, 24),
('introduction', 'set_relationship_status', 'set_relationship_status', '', 0, 25),
('introduction', 'use_furniture', 'switch_item_state', '', 0, 26),
('introduction', 'use_habbicon', 'use_habbicon', '', 0, 27),
('introduction', 'use_teleport', 'teleport', '', 0, 28),
('introduction', 'wave_at_user', 'wave', '', 0, 29),
('introduction', 'wear_badge', 'wear_badge', '', 0, 30);

INSERT IGNORE INTO reward_track_task_levels
(track_id, task_id, level, required_count, points_reward, premium) VALUES
('introduction', 'chat_with_users', 1, 5, 10, 0),
('introduction', 'chat_with_users', 2, 25, 20, 0),
('introduction', 'chat_with_users', 3, 100, 40, 0),
('introduction', 'visit_rooms', 1, 1, 10, 0),
('introduction', 'visit_rooms', 2, 5, 20, 0),
('introduction', 'visit_rooms', 3, 10, 40, 0),
('introduction', 'place_furniture', 1, 1, 10, 0),
('introduction', 'place_furniture', 2, 5, 20, 0),
('introduction', 'place_furniture', 3, 15, 40, 0),
('introduction', 'give_respect', 1, 1, 10, 0),
('introduction', 'give_respect', 2, 3, 20, 0),
('introduction', 'give_respect', 3, 5, 40, 0),
('introduction', 'buy_catalog_furni', 1, 1, 10, 0),
('introduction', 'buy_catalog_furni', 2, 5, 20, 0),
('introduction', 'buy_catalog_furni', 3, 20, 40, 0),
('introduction', 'change_motto', 1, 1, 10, 0),
('introduction', 'change_outfit', 1, 1, 10, 0),
('introduction', 'close_love_lock', 1, 1, 10, 0),
('introduction', 'create_room', 1, 1, 10, 0),
('introduction', 'dance_in_room', 1, 1, 10, 0),
('introduction', 'dance_in_room', 2, 5, 20, 0),
('introduction', 'dance_in_room', 3, 20, 40, 0),
('introduction', 'feed_pet', 1, 1, 10, 0),
('introduction', 'feed_pet', 2, 5, 20, 0),
('introduction', 'feed_pet', 3, 20, 40, 0),
('introduction', 'follow_friend', 1, 1, 10, 0),
('introduction', 'follow_friend', 2, 5, 20, 0),
('introduction', 'follow_friend', 3, 20, 40, 0),
('introduction', 'go_swimming', 1, 1, 10, 0),
('introduction', 'go_swimming', 2, 5, 20, 0),
('introduction', 'go_swimming', 3, 20, 40, 0),
('introduction', 'grab_drink', 1, 1, 10, 0),
('introduction', 'grab_drink', 2, 5, 20, 0),
('introduction', 'grab_drink', 3, 20, 40, 0),
('introduction', 'level_pet', 1, 1, 10, 0),
('introduction', 'level_pet', 2, 5, 20, 0),
('introduction', 'level_pet', 3, 20, 40, 0),
('introduction', 'make_friends', 1, 1, 10, 0),
('introduction', 'make_friends', 2, 5, 20, 0),
('introduction', 'make_friends', 3, 20, 40, 0),
('introduction', 'move_furniture', 1, 1, 10, 0),
('introduction', 'move_furniture', 2, 5, 20, 0),
('introduction', 'move_furniture', 3, 20, 40, 0),
('introduction', 'pet_a_pet', 1, 1, 10, 0),
('introduction', 'pet_a_pet', 2, 5, 20, 0),
('introduction', 'pet_a_pet', 3, 20, 40, 0),
('introduction', 'place_builders_club_furni', 1, 1, 10, 0),
('introduction', 'place_builders_club_furni', 2, 5, 20, 0),
('introduction', 'place_builders_club_furni', 3, 20, 40, 0),
('introduction', 'publish_picture', 1, 1, 10, 0),
('introduction', 'replenish_respect', 1, 1, 10, 0),
('introduction', 'rotate_furniture', 1, 1, 10, 0),
('introduction', 'rotate_furniture', 2, 5, 20, 0),
('introduction', 'rotate_furniture', 3, 20, 40, 0),
('introduction', 'send_messenger_invite', 1, 1, 10, 0),
('introduction', 'send_messenger_invite', 2, 5, 20, 0),
('introduction', 'send_messenger_invite', 3, 20, 40, 0),
('introduction', 'send_messenger_message', 1, 1, 10, 0),
('introduction', 'send_messenger_message', 2, 5, 20, 0),
('introduction', 'send_messenger_message', 3, 20, 40, 0),
('introduction', 'set_relationship_status', 1, 1, 10, 0),
('introduction', 'use_furniture', 1, 1, 10, 0),
('introduction', 'use_furniture', 2, 5, 20, 0),
('introduction', 'use_furniture', 3, 20, 40, 0),
('introduction', 'use_habbicon', 1, 1, 10, 0),
('introduction', 'use_habbicon', 2, 5, 20, 0),
('introduction', 'use_habbicon', 3, 20, 40, 0),
('introduction', 'use_teleport', 1, 1, 10, 0),
('introduction', 'use_teleport', 2, 5, 20, 0),
('introduction', 'use_teleport', 3, 20, 40, 0),
('introduction', 'wave_at_user', 1, 1, 10, 0),
('introduction', 'wave_at_user', 2, 5, 20, 0),
('introduction', 'wave_at_user', 3, 20, 40, 0),
('introduction', 'wear_badge', 1, 1, 10, 0);

INSERT IGNORE INTO reward_track_prizes
(track_id, id, required_points, product_item_type_id, reward_type, extra_params, reward_amount, premium, sort_order) VALUES
('introduction', 'track_champ', 50, 4, 'badge', 'ACH_RewardTracksCompleted1', 1, 0, 1),
('introduction', 'track_champ_premium', 200, 4, 'badge', 'ACH_RewardTracksCompleted2', 1, 1, 2);
