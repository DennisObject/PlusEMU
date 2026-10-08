-- Every activity point balance (Habbo's points types) lives in one table; credits stay in users.credits.
-- A missing row is a balance of 0. Duckets are type 0, diamonds 5 and GOTW points 103, the types the client shows them as.
CREATE TABLE IF NOT EXISTS user_currencies (
    user_id INT NOT NULL,
    type INT NOT NULL,
    amount INT NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, type),
    CONSTRAINT fk_user_currencies_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT chk_user_currencies_type CHECK (type >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

START TRANSACTION;
INSERT INTO user_currencies (user_id, type, amount)
SELECT id, 0, activity_points FROM users WHERE COALESCE(activity_points, 0) <> 0
ON DUPLICATE KEY UPDATE amount = VALUES(amount);
INSERT INTO user_currencies (user_id, type, amount)
SELECT id, 5, vip_points FROM users WHERE COALESCE(vip_points, 0) <> 0
ON DUPLICATE KEY UPDATE amount = VALUES(amount);
INSERT INTO user_currencies (user_id, type, amount)
SELECT id, 103, gotw_points FROM users WHERE COALESCE(gotw_points, 0) <> 0
ON DUPLICATE KEY UPDATE amount = VALUES(amount);
COMMIT;

ALTER TABLE users DROP COLUMN activity_points, DROP COLUMN vip_points, DROP COLUMN gotw_points;
