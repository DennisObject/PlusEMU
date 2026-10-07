-- SnowStorm (AIR SnowWar game). Apply while PlusEMU is stopped and back up the database first; Plus does not auto-run
-- SQL updates. Safe to rerun: tables are created once and tuned settings, offers and scores are kept.
-- Arenas are not stored here: they ship as snowstorm/arena_<fieldType>.json next to the emulator.

-- Score per user per UTC week (weeks start on Monday). All-time tables sum the weeks.
CREATE TABLE IF NOT EXISTS snowwar_scores (
    user_id INT NOT NULL,
    week_start DATE NOT NULL,
    score BIGINT NOT NULL DEFAULT 0,
    matches INT NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, week_start),
    KEY week_score (week_start, score)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Bought games and today's use of the free daily games.
CREATE TABLE IF NOT EXISTS snowwar_game_tokens (
    user_id INT NOT NULL PRIMARY KEY,
    games INT NOT NULL DEFAULT 0,
    free_games_date DATE NULL DEFAULT NULL,
    free_games_used INT NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The game hub's "get more games" packs (points_type 0 = duckets, 5 = diamonds).
CREATE TABLE IF NOT EXISTS snowwar_token_offers (
    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    localization_id VARCHAR(64) NOT NULL,
    price_credits INT NOT NULL DEFAULT 0,
    price_points INT NOT NULL DEFAULT 0,
    points_type INT NOT NULL DEFAULT 0,
    games INT NOT NULL DEFAULT 0,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    order_num INT NOT NULL DEFAULT 0,
    UNIQUE KEY localization_id (localization_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO snowwar_token_offers (localization_id, price_credits, price_points, points_type, games, order_num) VALUES
    ('GET_SNOWWAR_TOKENS', 10, 0, 0, 10, 1),
    ('GET_SNOWWAR_TOKENS2', 80, 0, 0, 100, 2),
    ('GET_SNOWWAR_TOKENS3', 200, 0, 0, 300, 3);

-- Polaris setting names. The emulator uses the same defaults when a row is missing, except that SnowStorm stays off
-- until gamecenter.snowwar.enabled is 1.
INSERT IGNORE INTO server_settings (`key`, `value`, `description`) VALUES
    ('gamecenter.snowwar.enabled', '1', 'SnowStorm on (1) or off (0).'),
    ('gamecenter.snowwar.players.min', '2', 'Players a SnowStorm lobby needs before its countdown starts.'),
    ('gamecenter.snowwar.queue.match.max', '8', 'Players per SnowStorm game (2-8, two teams).'),
    ('gamecenter.snowwar.games.max.concurrent', '1', 'SnowStorm games running at once; full lobbies wait in the arena queue.'),
    ('gamecenter.snowwar.game.start.time', '15', 'SnowStorm lobby and rematch countdown in seconds.'),
    ('gamecenter.snowwar.game.length.seconds', '180', 'SnowStorm match length in seconds.'),
    ('gamecenter.snowwar.preparing.seconds', '5', 'Seconds between StageStarting and StageRunning (the AIR countdown shows 5..1).'),
    ('gamecenter.snowwar.restart.seconds', '30', 'Seconds the SnowStorm results and rematch window stays open.'),
    ('gamecenter.snowwar.arenas', '8,9,11', 'SnowStorm arena field types played in rotation (8 Arctic Island, 9 Dragon Top, 11 Fight Night).'),
    ('gamecenter.games.free.daily', '10', 'Free SnowStorm games per user per UTC day; -1 for unlimited.'),
    ('gamecenter.game.leave.block.seconds', '180', 'Seconds a player who leaves a running SnowStorm game cannot join another.');
