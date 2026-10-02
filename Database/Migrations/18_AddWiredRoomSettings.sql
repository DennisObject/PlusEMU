-- Apply manually before enabling Wired room settings. Existing rooms columns are unchanged.
CREATE TABLE IF NOT EXISTS room_wired_settings (
    room_id INT UNSIGNED NOT NULL PRIMARY KEY,
    inspect_mask INT NOT NULL DEFAULT 2,
    modify_mask INT NOT NULL DEFAULT 2,
    timezone VARCHAR(64) NOT NULL DEFAULT ''
) ENGINE=InnoDB;
