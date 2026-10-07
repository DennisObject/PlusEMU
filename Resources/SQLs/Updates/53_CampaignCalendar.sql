CREATE TABLE IF NOT EXISTS campaign_calendars (
    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(64) COLLATE utf8mb4_bin NOT NULL UNIQUE,
    image VARCHAR(255) NOT NULL DEFAULT '',
    starts_at DATETIME(6) NOT NULL,
    days INT NOT NULL,
    enabled BOOL NOT NULL DEFAULT FALSE,
    lock_expired BOOL NOT NULL DEFAULT TRUE,
    hc_duckets_multiplier DOUBLE NOT NULL DEFAULT 2
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS campaign_calendar_rewards (
    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    campaign_id INT NOT NULL,
    product_name VARCHAR(255) NOT NULL,
    custom_image VARCHAR(255) NOT NULL DEFAULT '',
    credits INT NOT NULL DEFAULT 0,
    duckets INT NOT NULL DEFAULT 0,
    diamonds INT NOT NULL DEFAULT 0,
    badge VARCHAR(50) NOT NULL DEFAULT '',
    item_id INT UNSIGNED NOT NULL DEFAULT 0,
    hc_days INT NOT NULL DEFAULT 0,
    INDEX (campaign_id),
    FOREIGN KEY (campaign_id) REFERENCES campaign_calendars(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS user_calendar_claims (
    user_id INT NOT NULL,
    campaign_id INT NOT NULL,
    day INT NOT NULL,
    reward_id INT NOT NULL,
    claimed_at DATETIME(6) NOT NULL,
    PRIMARY KEY (user_id, campaign_id, day)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
