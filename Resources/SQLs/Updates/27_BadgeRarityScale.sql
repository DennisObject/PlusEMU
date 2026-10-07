-- Badge rarity ceilings scale with the active population: a tier holds badges with at most
-- max(min_owners, ceil(percent of active players)) owners. A single owner is always unique.
-- The emulator uses these defaults when the rows are missing.
INSERT IGNORE INTO server_settings (`key`, `value`, description) VALUES
 ('badge.rarity.active_days','90','Days since last login that still count a player as active'),
 ('badge.rarity.uncommon','0','Enable the uncommon tier (WIN63 badge_rarity.uncommon)'),
 ('badge.rarity.legendary.percent','0.5','Legendary: owners as a percentage of active players'),
 ('badge.rarity.legendary.min_owners','3','Legendary: owner ceiling on small hotels'),
 ('badge.rarity.mythical.percent','1.5','Mythical: owners as a percentage of active players'),
 ('badge.rarity.mythical.min_owners','8','Mythical: owner ceiling on small hotels'),
 ('badge.rarity.epic.percent','5','Epic: owners as a percentage of active players'),
 ('badge.rarity.epic.min_owners','20','Epic: owner ceiling on small hotels'),
 ('badge.rarity.rare.percent','15','Rare: owners as a percentage of active players'),
 ('badge.rarity.rare.min_owners','50','Rare: owner ceiling on small hotels'),
 ('badge.rarity.uncommon.percent','35','Uncommon: owners as a percentage of active players'),
 ('badge.rarity.uncommon.min_owners','0','Uncommon: owner ceiling on small hotels');
