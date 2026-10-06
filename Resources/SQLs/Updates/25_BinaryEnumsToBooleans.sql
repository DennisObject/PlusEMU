DELIMITER //
CREATE PROCEDURE `migrate_binary_enums_to_booleans`()
BEGIN
  DECLARE finished BOOL DEFAULT FALSE;
  DECLARE table_name_value VARCHAR(64);
  DECLARE column_name_value VARCHAR(64);
  DECLARE nullable_value VARCHAR(3);
  DECLARE default_value TEXT;
  DECLARE comment_value TEXT;
  DECLARE binary_enums CURSOR FOR
    SELECT `TABLE_NAME`, `COLUMN_NAME`, `IS_NULLABLE`, `COLUMN_DEFAULT`, `COLUMN_COMMENT`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `COLUMN_TYPE` IN ('enum(''0'',''1'')', 'enum(''1'',''0'')');
  DECLARE CONTINUE HANDLER FOR NOT FOUND SET finished = TRUE;

  OPEN binary_enums;
  enum_loop: LOOP
    FETCH binary_enums INTO table_name_value, column_name_value, nullable_value, default_value, comment_value;
    IF finished THEN LEAVE enum_loop; END IF;

    SET @table_identifier = REPLACE(table_name_value, '`', '``');
    SET @column_identifier = REPLACE(column_name_value, '`', '``');
    SET @nullability = IF(nullable_value = 'YES', ' NULL', ' NOT NULL');
    SET @varchar_default = CASE
      WHEN default_value IS NULL THEN ''
      WHEN default_value = 'NULL' AND nullable_value = 'YES' THEN ' DEFAULT NULL'
      WHEN default_value IN ('1', '''1''') THEN ' DEFAULT ''1'''
      ELSE ' DEFAULT ''0'''
    END;
    SET @boolean_default = CASE
      WHEN default_value IS NULL THEN ''
      WHEN default_value = 'NULL' AND nullable_value = 'YES' THEN ' DEFAULT NULL'
      WHEN default_value IN ('1', '''1''') THEN ' DEFAULT TRUE'
      ELSE ' DEFAULT FALSE'
    END;
    SET @comment_clause = IF(comment_value = '', '', CONCAT(' COMMENT ', QUOTE(comment_value)));

    SET @statement = CONCAT('ALTER TABLE `', @table_identifier, '` MODIFY COLUMN `', @column_identifier,
      '` VARCHAR(1) CHARACTER SET ascii', @nullability, @varchar_default, @comment_clause);
    PREPARE migration_statement FROM @statement;
    EXECUTE migration_statement;
    DEALLOCATE PREPARE migration_statement;

    SET @statement = CONCAT('ALTER TABLE `', @table_identifier, '` MODIFY COLUMN `', @column_identifier,
      '` BOOL', @nullability, @boolean_default, @comment_clause);
    PREPARE migration_statement FROM @statement;
    EXECUTE migration_statement;
    DEALLOCATE PREPARE migration_statement;
  END LOOP;
  CLOSE binary_enums;
END//
DELIMITER ;

CALL `migrate_binary_enums_to_booleans`();
DROP PROCEDURE `migrate_binary_enums_to_booleans`;
