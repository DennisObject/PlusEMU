-- Chest stock consists of existing inventory items or redeemed credit vouchers.
ALTER TABLE items ADD COLUMN IF NOT EXISTS chest_item_id INT UNSIGNED NULL,
    ADD COLUMN IF NOT EXISTS chest_transaction_id BIGINT UNSIGNED NULL,
    ADD COLUMN IF NOT EXISTS chest_random_order BIGINT UNSIGNED NULL;
CREATE TABLE IF NOT EXISTS wired_chests (
    item_id INT UNSIGNED NOT NULL PRIMARY KEY,
    kind TINYINT UNSIGNED NOT NULL,
    coins INT UNSIGNED NOT NULL DEFAULT 0,
    capacity_level INT UNSIGNED NOT NULL DEFAULT 0,
    settings LONGTEXT NOT NULL,
    CONSTRAINT wired_chest_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS wired_contracts (
    item_id INT UNSIGNED NOT NULL PRIMARY KEY,
    contract LONGTEXT NOT NULL,
    CONSTRAINT wired_contract_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- Transaction rows and entry item IDs are retained audit history, not live parent relationships.
CREATE TABLE IF NOT EXISTS wired_chest_transactions (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    operation_id CHAR(32) NOT NULL UNIQUE,
    user_id INT NOT NULL,
    room_id INT NOT NULL,
    source_id INT UNSIGNED NOT NULL,
    result LONGTEXT NOT NULL,
    created_at DATETIME(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS wired_chest_transaction_entries (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    transaction_id BIGINT UNSIGNED NOT NULL,
    chest_id INT UNSIGNED NOT NULL,
    item_id INT UNSIGNED NULL,
    is_deposit TINYINT(1) NOT NULL,
    coins INT UNSIGNED NOT NULL DEFAULT 0,
    CONSTRAINT wired_chest_entry_log FOREIGN KEY (transaction_id) REFERENCES wired_chest_transactions(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- Normalize existing companion tables as well as newly created ones.
ALTER TABLE wired_chests CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE wired_contracts CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE wired_chest_transactions MODIFY COLUMN room_id INT NOT NULL,
    CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE wired_chest_transaction_entries CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
-- Stored chest stock has no inventory owner; zero remains the existing runtime sentinel.
-- Apply after the schema modernization owner key, including in the staged fresh-install script.
ALTER TABLE items DROP FOREIGN KEY IF EXISTS fk_items_user_id,
    ADD COLUMN IF NOT EXISTS user_ref INT GENERATED ALWAYS AS (NULLIF(user_id, 0)) STORED;
CREATE INDEX IF NOT EXISTS items_user_ref ON items(user_ref);
SET @wired_owner_fk_sql = IF(EXISTS(SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='items' AND CONSTRAINT_NAME='fk_items_user_ref'),
    'SELECT 1', 'ALTER TABLE items ADD CONSTRAINT fk_items_user_ref FOREIGN KEY (user_ref) REFERENCES users(id) ON DELETE CASCADE');
PREPARE wired_owner_fk FROM @wired_owner_fk_sql;
EXECUTE wired_owner_fk;
DEALLOCATE PREPARE wired_owner_fk;
CREATE INDEX IF NOT EXISTS items_chest ON items(chest_item_id,chest_transaction_id);
SET @wired_chest_fk_sql = IF(EXISTS(SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='items' AND CONSTRAINT_NAME='wired_stored_item_chest'),
    'SELECT 1', 'ALTER TABLE items ADD CONSTRAINT wired_stored_item_chest FOREIGN KEY (chest_item_id) REFERENCES wired_chests(item_id) ON DELETE RESTRICT');
PREPARE wired_chest_fk FROM @wired_chest_fk_sql;
EXECUTE wired_chest_fk;
DEALLOCATE PREPARE wired_chest_fk;
UPDATE furniture SET interaction_type='wired_chest_furni' WHERE item_name IN ('wf_storage_furni1','wf_storage_furni2','wf_storage_furni_starter');
UPDATE furniture SET interaction_type='wired_chest_coins' WHERE item_name IN ('wf_storage_coins1','wf_storage_coins2');
UPDATE furniture SET interaction_type='wired_contract_payment' WHERE item_name='wf_contract_payment';
UPDATE furniture SET interaction_type='wired_contract_reward' WHERE item_name='wf_contract_reward';
UPDATE furniture SET interaction_type='wired_contract_trade' WHERE item_name='wf_contract_trade';
UPDATE furniture SET interaction_type='wired_effect' WHERE item_name IN ('wf_act_give_currency','wf_act_give_furni','wf_act_init_transaction','wf_act_cancel_transaction');
UPDATE furniture SET interaction_type='wired_condition' WHERE item_name IN ('wf_cnd_chest_has_items','wf_cnd_chest_has_item_type');
UPDATE furniture SET interaction_type='wired_trigger' WHERE item_name IN ('wf_trg_transaction_complete','wf_trg_transaction_fail');
UPDATE furniture SET interaction_type='wired_addon' WHERE item_name IN ('wf_xtra_custom_contract','wf_xtra_scan_chest_furni_by_type');
