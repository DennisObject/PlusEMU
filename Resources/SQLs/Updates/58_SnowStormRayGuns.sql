-- SnowStorm ray guns (Plus extra from Polaris, not in the official client): standing on the tile behind an
-- ads_igorraygun fires a burst of seven snowballs. Apply after 57_SnowStorm.sql; safe to rerun, a tuned value is kept.
INSERT IGNORE INTO server_settings (`key`, `value`, `description`) VALUES
    ('gamecenter.snowwar.raygun.enabled', '1', 'SnowStorm ray guns on (1) or off (0); a Plus extra, not in the official client.');
