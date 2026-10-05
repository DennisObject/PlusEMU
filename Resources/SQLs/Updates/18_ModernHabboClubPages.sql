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
