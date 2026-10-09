UPDATE furniture SET interaction_type = 'area_hide'
WHERE item_name = 'conf_area_hide' AND type = 's' AND sprite_id = 15215
  AND interaction_type IN ('default', 'default_floor', 'conf_area_hide');
