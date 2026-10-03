-- Apply while PlusEMU is stopped; SQL updates are not automatic.
-- Offers shown on club_buy/vip_buy catalog pages, and the club time users bought there.
CREATE TABLE IF NOT EXISTS catalog_club_offers (
 id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 enabled TINYINT(1) NOT NULL DEFAULT 1,
 name VARCHAR(64) NOT NULL,
 days INT NOT NULL,
 credits INT NOT NULL DEFAULT 0,
 points INT NOT NULL DEFAULT 0,
 points_type INT NOT NULL DEFAULT 0,
 type ENUM('HC','VIP') NOT NULL DEFAULT 'HC',
 giftable TINYINT(1) NOT NULL DEFAULT 0
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS user_club_memberships (
 user_id INT NOT NULL PRIMARY KEY,
 expires_at INT NOT NULL
) ENGINE=InnoDB;
-- Same lengths and prices as the old DEAL_HC furni on the Buy Club page.
INSERT INTO catalog_club_offers (id, name, days, credits, type)
SELECT * FROM (SELECT 1, 'HABBO_CLUB_1_MONTH', 31, 100, 'HC' UNION ALL
               SELECT 2, 'HABBO_CLUB_3_MONTHS', 93, 250, 'HC' UNION ALL
               SELECT 3, 'HABBO_CLUB_6_MONTHS', 186, 500, 'HC') AS offers
WHERE NOT EXISTS (SELECT 1 FROM catalog_club_offers);
-- The Habbo Club page and its Buy Club child render the club purchase list.
-- hc_membership is the page name the client opens from the toolbar and HC center.
UPDATE catalog_pages SET page_layout = 'club_buy' WHERE id = 5 AND page_link = 'habbo_club';
UPDATE catalog_pages SET page_layout = 'club_buy', page_link = 'hc_membership' WHERE id = 7 AND caption = 'Buy Club';
