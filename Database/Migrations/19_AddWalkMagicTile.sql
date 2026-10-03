-- Apply manually; restart/reload furniture and catalogue caches afterwards.
-- Deployed plus-hotel/web/entrypoint.sh links /gamedata/FurnitureData.json to the
-- mounted nitro-assets/furniture/json/FurnitureData.json: tile_walkmagic = 14270.
-- Hotels with different furnidata must set @walk_magic_sprite_id before running.
-- IDs are allocated by the database. Re-running does not duplicate the offer.
SET @walk_magic_sprite_id = COALESCE(@walk_magic_sprite_id, 14270);

INSERT INTO furniture (item_name, public_name, type, width, length, stack_height,
    can_stack, can_sit, is_walkable, sprite_id, interaction_type)
SELECT 'tile_walkmagic', 'Walk Magic Tile', 's', 1, 1, 0, '1', '0', '1',
    @walk_magic_sprite_id, 'tile_walkmagic'
WHERE NOT EXISTS (SELECT 1 FROM furniture WHERE item_name = 'tile_walkmagic');

UPDATE furniture SET interaction_type = 'tile_walkmagic', stack_height = 0,
    can_stack = '1', can_sit = '0', is_walkable = '1', sprite_id = @walk_magic_sprite_id
WHERE item_name = 'tile_walkmagic';
SET @walk_magic_item_id = (SELECT MIN(id) FROM furniture WHERE item_name = 'tile_walkmagic');

-- Uses the existing catalog_pages / catalog_items seed schema without fixed IDs.
INSERT INTO catalog_pages (parent_id, caption, icon_image, visible, enabled,
    min_rank, min_vip, order_num, page_link, page_layout, page_strings_1, page_strings_2)
SELECT -1, 'Room Building', 28, '1', '1', 1, 0, 1000, 'room_building',
    'default_3x3', 'stackers|stackk', 'Build paths under or over furniture with the Walk Magic Tile.|'
WHERE NOT EXISTS (SELECT 1 FROM catalog_pages WHERE page_link = 'room_building' OR caption = 'Room Building');
SET @walk_magic_page_id = (SELECT MIN(id) FROM catalog_pages WHERE page_link = 'room_building' OR caption = 'Room Building');

INSERT INTO catalog_items (page_id, item_id, catalog_name, cost_credits, amount,
    offer_active, extradata)
SELECT @walk_magic_page_id, CAST(@walk_magic_item_id AS CHAR), 'tile_walkmagic', 3, 1, '1', '0'
WHERE NOT EXISTS (SELECT 1 FROM catalog_items WHERE catalog_name = 'tile_walkmagic');
