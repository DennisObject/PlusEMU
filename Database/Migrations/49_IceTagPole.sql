-- Restore only the official pole's missing interaction; retain custom definitions and all IDs.
UPDATE furniture SET interaction_type = 'icetag_pole'
WHERE item_name = 'es_tagging' AND sprite_id = 3741 AND interaction_type = 'default';
