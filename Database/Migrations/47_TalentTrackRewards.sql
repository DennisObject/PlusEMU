SET @talent_track_type_ddl = IF(
  EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'talents_sub_levels' AND COLUMN_NAME = 'talent_type'),
  'SELECT 1',
  'ALTER TABLE talents_sub_levels ADD COLUMN talent_type ENUM(''citizenship'',''helper'') NOT NULL DEFAULT ''citizenship'' AFTER id'
);
PREPARE talent_track_type_statement FROM @talent_track_type_ddl;
EXECUTE talent_track_type_statement;
DEALLOCATE PREPARE talent_track_type_statement;

CREATE TABLE IF NOT EXISTS user_talent_rewards (
  user_id INT NOT NULL,
  type ENUM('citizenship','helper') NOT NULL,
  level INT NOT NULL,
  PRIMARY KEY (user_id, type, level)
) ENGINE=InnoDB;
