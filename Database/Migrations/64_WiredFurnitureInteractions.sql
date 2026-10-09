-- Store the concrete Wired interaction on official furniture rows.
-- Asset names, IDs, offers, owned items and persisted box_name/configuration stay unchanged.
UPDATE furniture SET interaction_type = 'wf_xtra_var_fx_health'
WHERE item_name = 'wf_xtra_varfx_hp' AND type = 's' AND sprite_id = 19196
  AND interaction_type IN ('default', 'wired_addon', 'wf_xtra_varfx_hp');

UPDATE furniture SET interaction_type = 'wf_xtra_var_fx_progress'
WHERE item_name = 'wf_xtra_varfx_prog' AND type = 's' AND sprite_id = 19199
  AND interaction_type IN ('default', 'wired_addon', 'wf_xtra_varfx_prog');

UPDATE furniture SET interaction_type = 'wf_xtra_var_fx_level'
WHERE item_name = 'wf_xtra_varfx_levelling' AND type = 's' AND sprite_id = 19197
  AND interaction_type IN ('default', 'wired_addon', 'wf_xtra_varfx_levelling');

UPDATE furniture SET interaction_type = 'wf_xtra_var_fx_status'
WHERE item_name = 'wf_xtra_varfx_status' AND type = 's' AND sprite_id = 19200
  AND interaction_type IN ('default', 'wired_addon', 'wf_xtra_varfx_status');

UPDATE furniture SET interaction_type = 'wf_xtra_var_fx_boss'
WHERE item_name = 'wf_xtra_varfx_boss' AND type = 's' AND sprite_id = 19195
  AND interaction_type IN ('default', 'wired_addon', 'wf_xtra_varfx_boss');

UPDATE furniture SET interaction_type = 'wf_xtra_var_fx_number'
WHERE item_name = 'wf_xtra_varfx_number' AND type = 's' AND sprite_id = 19198
  AND interaction_type IN ('default', 'wired_addon', 'wf_xtra_varfx_number');

UPDATE furniture SET interaction_type = 'wf_trg_at_given_time'
WHERE item_name = 'wf_proto_trg_at_given_time' AND type = 's' AND sprite_id = 17678
  AND interaction_type IN ('default', 'wired_trigger', 'wf_proto_trg_at_given_time');

UPDATE furniture SET interaction_type = 'wf_cnd_trggrer_on_frn'
WHERE item_name = 'wf_proto_cnd_trggrer_on_frn' AND type = 's' AND sprite_id = 17679
  AND interaction_type IN ('default', 'wired_condition', 'wf_proto_cnd_trggrer_on_frn');

UPDATE furniture SET interaction_type = 'wf_act_toggle_state'
WHERE item_name = 'wf_ltdproto_act_toggle_state' AND type = 's' AND sprite_id = 17676
  AND interaction_type IN ('default', 'wired_effect', 'wf_ltdproto_act_toggle_state');
