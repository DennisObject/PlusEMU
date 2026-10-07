CREATE TABLE IF NOT EXISTS crafting_recipes (
    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    code VARCHAR(128) COLLATE utf8mb4_bin NOT NULL UNIQUE,
    product_code VARCHAR(128) COLLATE utf8mb4_bin NOT NULL,
    reward_item_id INT UNSIGNED NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    secret BOOLEAN NOT NULL DEFAULT FALSE,
    remaining INT NULL DEFAULT NULL,
    achievement VARCHAR(128) NOT NULL DEFAULT '',
    CHECK (remaining IS NULL OR remaining >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS crafting_altars_recipes (
    altar_item_id INT UNSIGNED NOT NULL,
    recipe_id INT NOT NULL,
    PRIMARY KEY (altar_item_id, recipe_id),
    FOREIGN KEY (recipe_id) REFERENCES crafting_recipes(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS crafting_recipes_ingredients (
    recipe_id INT NOT NULL,
    item_id INT UNSIGNED NOT NULL,
    amount INT NOT NULL,
    PRIMARY KEY (recipe_id, item_id),
    FOREIGN KEY (recipe_id) REFERENCES crafting_recipes(id),
    CHECK (amount BETWEEN 1 AND 50)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS user_crafting_recipes (
    user_id INT NOT NULL,
    recipe_id INT NOT NULL,
    PRIMARY KEY (user_id, recipe_id),
    FOREIGN KEY (recipe_id) REFERENCES crafting_recipes(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- The original 24 Halloween/crystal recipes from Polaris 34bc0d49511c659fd0054d35d957669ebd6ce9ed,
-- Emulator/src/main/resources/db/migration/V20260518000000__base_database.sql.
-- Source item IDs are resolved by exact class name and sprite ID, never copied as local IDs.
-- Existing configured codes are preserved. Missing or ambiguous furniture leaves a recipe unseeded.
DROP TEMPORARY TABLE IF EXISTS crafting_seed_new,crafting_seed_ingredients,crafting_seed_recipes;
CREATE TEMPORARY TABLE crafting_seed_recipes (
    code VARCHAR(128) COLLATE utf8mb4_bin PRIMARY KEY,
    reward_name VARCHAR(70) COLLATE utf8mb4_bin NOT NULL,
    reward_sprite INT NOT NULL,
    secret BOOLEAN NOT NULL
) DEFAULT CHARSET=utf8mb4;
INSERT INTO crafting_seed_recipes VALUES
    ('clothing_firehelm','clothing_firehelm',8342,FALSE),
    ('clothing_airhelm','clothing_airhelm',8340,FALSE),
    ('clothing_waterhelm','clothing_waterhelm',8336,FALSE),
    ('clothing_earthhelm','clothing_earthhelm',8346,FALSE),
    ('hween_c15_purecrystal2','hween_c15_purecrystal2',8363,TRUE),
    ('hween_c15_purecrystal3','hween_c15_purecrystal3',8373,TRUE),
    ('hween_c15_evilcrystal2','hween_c15_evilcrystal2',8362,TRUE),
    ('hween_c15_evilcrystal3','hween_c15_evilcrystal3',8365,TRUE),
    ('gothic_c15_chandelier','gothic_c15_chandelier',8230,TRUE),
    ('hween12_guillotine','hween12_guillotine',4738,TRUE),
    ('hween14_mariachi','hween14_mariachi',6179,TRUE),
    ('hween14_doll3','hween14_doll3',6203,TRUE),
    ('hween14_doll4','hween14_doll4',6204,TRUE),
    ('clothing_wavy2','clothing_wavy2',6286,TRUE),
    ('guitar_skull','guitar_skull',4011,TRUE),
    ('fxbox_fx152','fxbox_fx152',6322,TRUE),
    ('LT_skull','LT_skull',3189,TRUE),
    ('hween14_skelepieces','hween14_skelepieces',6178,TRUE),
    ('hween13_bldtrail','hween13_bldtrail',5300,TRUE),
    ('penguin_glow','penguin_glow',2993,TRUE),
    ('skullcandle','skullcandle',207,TRUE),
    ('fxbox_fx125','fxbox_fx125',6324,TRUE),
    ('deadduck','deadduck',208,TRUE),
    ('qt_xm10_iceduck','qt_xm10_iceduck',3727,TRUE);
CREATE TEMPORARY TABLE crafting_seed_ingredients (
    code VARCHAR(128) COLLATE utf8mb4_bin NOT NULL,
    item_name VARCHAR(70) COLLATE utf8mb4_bin NOT NULL,
    sprite_id INT NOT NULL,
    amount INT NOT NULL
) DEFAULT CHARSET=utf8mb4;
INSERT INTO crafting_seed_ingredients VALUES
    ('clothing_firehelm','hween_c15_purecrystal3',8373,4),
    ('clothing_airhelm','hween_c15_purecrystal2',8363,4),
    ('clothing_airhelm','hween_c15_purecrystal3',8373,4),
    ('clothing_waterhelm','hween_c15_purecrystal2',8363,8),
    ('clothing_earthhelm','hween_c15_purecrystal1',8404,4),
    ('clothing_earthhelm','hween_c15_purecrystal2',8363,4),
    ('hween_c15_purecrystal2','hween_c15_purecrystal1',8404,3),
    ('hween_c15_purecrystal3','hween_c15_purecrystal2',8363,3),
    ('hween_c15_evilcrystal2','hween_c15_evilcrystal1',8370,3),
    ('hween_c15_evilcrystal3','hween_c15_evilcrystal2',8362,3),
    ('gothic_c15_chandelier','rela_candles1',3234,2),
    ('gothic_c15_chandelier','hween09_chandelier',3294,1),
    ('gothic_c15_chandelier','hween_c15_evilcrystal2',8362,1),
    ('gothic_c15_chandelier','LT_skull',3189,6),
    ('hween12_guillotine','xmas11_elewood',4309,2),
    ('hween12_guillotine','hween13_bldtrail',5300,1),
    ('hween12_guillotine','xmas11_firewood',4292,4),
    ('hween12_guillotine','hween_c15_evilcrystal3',8365,1),
    ('hween14_mariachi','LT_skull',3189,1),
    ('hween14_mariachi','studio_guitar',4083,1),
    ('hween14_mariachi','hween_c15_purecrystal2',8363,1),
    ('hween14_mariachi','hween14_skelepieces',6178,3),
    ('hween14_doll3','hween14_skelepieces',6178,3),
    ('hween14_doll3','hween_c15_purecrystal2',8363,1),
    ('hween14_doll3','duck_afro',5002,1),
    ('hween14_doll4','duck',179,1),
    ('hween14_doll4','hween_c15_purecrystal2',8363,1),
    ('hween14_doll4','hween14_skelepieces',6178,3),
    ('clothing_wavy2','hween_c15_purecrystal2',8363,1),
    ('clothing_wavy2','vikings_spike',5869,1),
    ('clothing_wavy2','bathroom_shampoo',6153,2),
    ('guitar_skull','rela_rock',3248,1),
    ('guitar_skull','hween_c15_purecrystal1',8404,1),
    ('guitar_skull','duck_afro',5002,1),
    ('fxbox_fx152','hween12_guillotine',4738,1),
    ('fxbox_fx152','hween_c15_evilcrystal3',8365,1),
    ('fxbox_fx152','hween14_goat',6191,1),
    ('LT_skull','deadduck2',209,2),
    ('LT_skull','hween_c15_purecrystal1',8404,1),
    ('hween14_skelepieces','deadduck',208,3),
    ('hween14_skelepieces','hween_c15_purecrystal1',8404,1),
    ('hween13_bldtrail','deadduck',208,2),
    ('hween13_bldtrail','hween_c15_purecrystal1',8404,1),
    ('penguin_glow','hween_c15_evilcrystal2',8362,1),
    ('penguin_glow','hween08_manhole',2946,1),
    ('penguin_glow','penguin_basic',2977,1),
    ('skullcandle','rela_candle1',3238,1),
    ('skullcandle','LT_skull',3189,1),
    ('fxbox_fx125','hween11_pumpkin',4268,1),
    ('fxbox_fx125','hween_c15_evilcrystal2',8362,1),
    ('deadduck','duck',179,1),
    ('deadduck','hween_c15_evilcrystal1',8370,1),
    ('qt_xm10_iceduck','duck',179,1),
    ('qt_xm10_iceduck','hween_c15_purecrystal1',8404,1);
CREATE TEMPORARY TABLE crafting_seed_new AS
SELECT seed.code,reward.id AS reward_id,seed.secret,altar.id AS altar_id
FROM crafting_seed_recipes seed
JOIN furniture reward ON BINARY reward.item_name=BINARY seed.reward_name AND reward.sprite_id=seed.reward_sprite
JOIN furniture altar ON BINARY altar.item_name=BINARY 'hween_c15_altar' AND altar.sprite_id=8388
WHERE (SELECT COUNT(*) FROM furniture item WHERE BINARY item.item_name=BINARY seed.reward_name AND item.sprite_id=seed.reward_sprite)=1
  AND (SELECT COUNT(*) FROM furniture item WHERE BINARY item.item_name=BINARY 'hween_c15_altar' AND item.sprite_id=8388)=1
  AND NOT EXISTS (SELECT 1 FROM crafting_recipes existing WHERE BINARY existing.code=BINARY seed.code)
  AND NOT EXISTS (
      SELECT 1 FROM crafting_seed_ingredients ingredient
      WHERE BINARY ingredient.code=BINARY seed.code
        AND (SELECT COUNT(*) FROM furniture item WHERE BINARY item.item_name=BINARY ingredient.item_name AND item.sprite_id=ingredient.sprite_id)<>1
  );
START TRANSACTION;
INSERT INTO crafting_recipes(code,product_code,reward_item_id,secret)
SELECT code,code,reward_id,secret FROM crafting_seed_new;
INSERT INTO crafting_altars_recipes(altar_item_id,recipe_id)
SELECT seed.altar_id,recipe.id FROM crafting_seed_new seed JOIN crafting_recipes recipe ON BINARY recipe.code=BINARY seed.code;
INSERT INTO crafting_recipes_ingredients(recipe_id,item_id,amount)
SELECT recipe.id,item.id,ingredient.amount FROM crafting_seed_new seed
JOIN crafting_recipes recipe ON BINARY recipe.code=BINARY seed.code
JOIN crafting_seed_ingredients ingredient ON BINARY ingredient.code=BINARY seed.code
JOIN furniture item ON BINARY item.item_name=BINARY ingredient.item_name AND item.sprite_id=ingredient.sprite_id;
COMMIT;
DROP TEMPORARY TABLE crafting_seed_new,crafting_seed_ingredients,crafting_seed_recipes;
