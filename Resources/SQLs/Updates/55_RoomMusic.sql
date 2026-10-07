CREATE TABLE IF NOT EXISTS room_music_songs (
    id INT NOT NULL PRIMARY KEY,
    code VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL UNIQUE,
    name VARCHAR(255) NOT NULL,
    creator VARCHAR(255) NOT NULL,
    trax_data MEDIUMTEXT NOT NULL,
    length_ms INT NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS room_music_disc_definitions (
    base_item INT UNSIGNED NOT NULL PRIMARY KEY,
    song_id INT NOT NULL,
    FOREIGN KEY (song_id) REFERENCES room_music_songs(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS room_music_players (
    item_id INT UNSIGNED NOT NULL PRIMARY KEY,
    start_index INT NOT NULL DEFAULT 0,
    version BIGINT NOT NULL DEFAULT 0,
    started_at DATETIME(6) NULL,
    FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS room_music_playlist (
    player_id INT UNSIGNED NOT NULL,
    disc_id INT UNSIGNED NOT NULL UNIQUE,
    position INT NOT NULL,
    song_id INT NOT NULL,
    PRIMARY KEY (player_id, position),
    FOREIGN KEY (player_id) REFERENCES room_music_players(item_id),
    FOREIGN KEY (disc_id) REFERENCES items(id),
    FOREIGN KEY (song_id) REFERENCES room_music_songs(id)
) ENGINE=InnoDB;
