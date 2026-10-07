-- Recycler stays closed until an operator supplies a complete prize distribution and enables it.
CREATE TABLE IF NOT EXISTS recycler_settings (
    id INT NOT NULL PRIMARY KEY,
    enabled BOOL NOT NULL DEFAULT FALSE,
    slots INT NOT NULL DEFAULT 5,
    cooldown_seconds INT NOT NULL DEFAULT 0
) ENGINE=InnoDB;
INSERT IGNORE INTO recycler_settings(id,enabled,slots,cooldown_seconds) VALUES(1,FALSE,5,0);

CREATE TABLE IF NOT EXISTS recycler_levels (
    level INT NOT NULL PRIMARY KEY,
    chance INT NOT NULL
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS recycler_prizes (
    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    level INT NOT NULL,
    item_id INT UNSIGNED NOT NULL,
    UNIQUE KEY recycler_level_item(level,item_id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS user_recycler (
    user_id INT NOT NULL PRIMARY KEY,
    next_allowed_at DATETIME(6) NOT NULL
) ENGINE=InnoDB;
