-- Apply while PlusEMU is stopped; SQL updates are not automatic.
-- Modern Habbo Club is rendered by vip_buy; club_buy is the legacy two-tier page.
-- Keep the configured subscription lengths and prices unchanged.
UPDATE catalog_pages
SET page_layout = 'vip_buy', caption = 'Habbo Club',
    page_strings_2 = 'Get exclusive clothes and hair styles and so much more!|'
WHERE id = 5 AND page_link = 'habbo_club' AND page_layout = 'club_buy';

UPDATE catalog_pages
SET page_layout = 'vip_buy',
    page_strings_2 = 'Get exclusive clothes and hair styles and so much more!|'
WHERE id = 7 AND page_link = 'hc_membership' AND page_layout = 'club_buy';

-- The protocol's VIP flag identifies modern HC offers. Do not change custom offers.
UPDATE catalog_club_offers
SET type = 'VIP'
WHERE (id = 1 AND name = 'HABBO_CLUB_1_MONTH')
   OR (id = 2 AND name = 'HABBO_CLUB_3_MONTHS')
   OR (id = 3 AND name = 'HABBO_CLUB_6_MONTHS');
