-- Official purchase IDs from the reviewed Wired FurniData (SHA-256 306b340f423abc762bbafc040ce447cc848769a69fa377de0209aa3abac7396c).
-- Repair missing IDs only. Prices, visibility, access, existing positive IDs and item identities stay unchanged.
UPDATE catalog_items AS item
JOIN furniture AS furni ON item.item_id = CAST(furni.id AS CHAR)
JOIN (
    SELECT 'wf_act_adjust_clock' AS item_name, 14555 AS sprite_id, 28809 AS offer_id
UNION ALL
    SELECT 'wf_act_bot_clothes' AS item_name, 7848 AS sprite_id, 16064 AS offer_id
UNION ALL
    SELECT 'wf_act_bot_follow_avatar' AS item_name, 7851 AS sprite_id, 16067 AS offer_id
UNION ALL
    SELECT 'wf_act_bot_give_handitem' AS item_name, 7852 AS sprite_id, 16068 AS offer_id
UNION ALL
    SELECT 'wf_act_bot_move' AS item_name, 7853 AS sprite_id, 16069 AS offer_id
UNION ALL
    SELECT 'wf_act_bot_talk' AS item_name, 7857 AS sprite_id, 16073 AS offer_id
UNION ALL
    SELECT 'wf_act_bot_talk_to_avatar' AS item_name, 7855 AS sprite_id, 16071 AS offer_id
UNION ALL
    SELECT 'wf_act_bot_teleport' AS item_name, 7849 AS sprite_id, 16065 AS offer_id
UNION ALL
    SELECT 'wf_act_call_stacks' AS item_name, 4834 AS sprite_id, 11865 AS offer_id
UNION ALL
    SELECT 'wf_act_chase' AS item_name, 5055 AS sprite_id, 12519 AS offer_id
UNION ALL
    SELECT 'wf_act_click_conf' AS item_name, 18673 AS sprite_id, 34242 AS offer_id
UNION ALL
    SELECT 'wf_act_control_clock' AS item_name, 13390 AS sprite_id, 26586 AS offer_id
UNION ALL
    SELECT 'wf_act_flee' AS item_name, 5061 AS sprite_id, 12525 AS offer_id
UNION ALL
    SELECT 'wf_act_freeze' AS item_name, 14126 AS sprite_id, 28193 AS offer_id
UNION ALL
    SELECT 'wf_act_furni_to_furni' AS item_name, 14328 AS sprite_id, 28415 AS offer_id
UNION ALL
    SELECT 'wf_act_furni_to_user' AS item_name, 14340 AS sprite_id, 28427 AS offer_id
UNION ALL
    SELECT 'wf_act_give_score' AS item_name, 3697 AS sprite_id, 9132 AS offer_id
UNION ALL
    SELECT 'wf_act_give_score_tm' AS item_name, 5043 AS sprite_id, 12507 AS offer_id
UNION ALL
    SELECT 'wf_act_join_team' AS item_name, 5062 AS sprite_id, 12526 AS offer_id
UNION ALL
    SELECT 'wf_act_kick_user' AS item_name, 4947 AS sprite_id, 12303 AS offer_id
UNION ALL
    SELECT 'wf_act_leave_team' AS item_name, 5049 AS sprite_id, 12513 AS offer_id
UNION ALL
    SELECT 'wf_act_log' AS item_name, 17599 AS sprite_id, 32843 AS offer_id
UNION ALL
    SELECT 'wf_act_match_to_sshot' AS item_name, 3700 AS sprite_id, 9135 AS offer_id
UNION ALL
    SELECT 'wf_act_move_furni_as_group' AS item_name, 18783 AS sprite_id, 34390 AS offer_id
UNION ALL
    SELECT 'wf_act_move_rotate' AS item_name, 3663 AS sprite_id, 9098 AS offer_id
UNION ALL
    SELECT 'wf_act_move_to_dir' AS item_name, 5048 AS sprite_id, 12512 AS offer_id
UNION ALL
    SELECT 'wf_act_neg_call_stacks' AS item_name, 14557 AS sprite_id, 28811 AS offer_id
UNION ALL
    SELECT 'wf_act_neg_log' AS item_name, 17598 AS sprite_id, 32842 AS offer_id
UNION ALL
    SELECT 'wf_act_neg_send_signal' AS item_name, 14556 AS sprite_id, 28810 AS offer_id
UNION ALL
    SELECT 'wf_act_place_furni' AS item_name, 18784 AS sprite_id, 34391 AS offer_id
UNION ALL
    SELECT 'wf_act_rel_mov' AS item_name, 14127 AS sprite_id, 28194 AS offer_id
UNION ALL
    SELECT 'wf_act_remove_furni' AS item_name, 18785 AS sprite_id, 34392 AS offer_id
UNION ALL
    SELECT 'wf_act_reset_timers' AS item_name, 3691 AS sprite_id, 9126 AS offer_id
UNION ALL
    SELECT 'wf_act_send_signal' AS item_name, 14085 AS sprite_id, 28103 AS offer_id
UNION ALL
    SELECT 'wf_act_set_altitude' AS item_name, 14092 AS sprite_id, 28110 AS offer_id
UNION ALL
    SELECT 'wf_act_show_message' AS item_name, 3681 AS sprite_id, 9116 AS offer_id
UNION ALL
    SELECT 'wf_act_teleport_to' AS item_name, 3674 AS sprite_id, 9109 AS offer_id
UNION ALL
    SELECT 'wf_act_teleport_to_room' AS item_name, 16743 AS sprite_id, 31666 AS offer_id
UNION ALL
    SELECT 'wf_act_toggle_state' AS item_name, 3685 AS sprite_id, 9120 AS offer_id
UNION ALL
    SELECT 'wf_act_unfreeze' AS item_name, 14130 AS sprite_id, 28197 AS offer_id
UNION ALL
    SELECT 'wf_cnd_actor_dir' AS item_name, 14129 AS sprite_id, 28196 AS offer_id
UNION ALL
    SELECT 'wf_cnd_actor_in_group' AS item_name, 4281 AS sprite_id, 10597 AS offer_id
UNION ALL
    SELECT 'wf_cnd_actor_in_team' AS item_name, 5056 AS sprite_id, 12520 AS offer_id
UNION ALL
    SELECT 'wf_cnd_counter_time_matches' AS item_name, 13810 AS sprite_id, 27637 AS offer_id
UNION ALL
    SELECT 'wf_cnd_date_rng_active' AS item_name, 5861 AS sprite_id, 13753 AS offer_id
UNION ALL
    SELECT 'wf_cnd_furnis_hv_avtrs' AS item_name, 3692 AS sprite_id, 9127 AS offer_id
UNION ALL
    SELECT 'wf_cnd_has_altitude' AS item_name, 14093 AS sprite_id, 28111 AS offer_id
UNION ALL
    SELECT 'wf_cnd_has_furni_on' AS item_name, 3857 AS sprite_id, 9558 AS offer_id
UNION ALL
    SELECT 'wf_cnd_has_handitem' AS item_name, 7854 AS sprite_id, 16070 AS offer_id
UNION ALL
    SELECT 'wf_cnd_match_date' AS item_name, 13803 AS sprite_id, 27630 AS offer_id
UNION ALL
    SELECT 'wf_cnd_match_snapshot' AS item_name, 3695 AS sprite_id, 9130 AS offer_id
UNION ALL
    SELECT 'wf_cnd_match_time' AS item_name, 13804 AS sprite_id, 27631 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_furni_on' AS item_name, 5440 AS sprite_id, 13121 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_has_handitem' AS item_name, 13806 AS sprite_id, 27633 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_hv_avtrs' AS item_name, 5441 AS sprite_id, 13122 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_in_group' AS item_name, 5448 AS sprite_id, 13129 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_in_team' AS item_name, 5439 AS sprite_id, 13120 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_match_snap' AS item_name, 5452 AS sprite_id, 13133 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_stuff_is' AS item_name, 5449 AS sprite_id, 13130 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_trggrer_on' AS item_name, 5438 AS sprite_id, 13119 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_triggerer_match' AS item_name, 13808 AS sprite_id, 27635 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_user_count' AS item_name, 5443 AS sprite_id, 13124 AS offer_id
UNION ALL
    SELECT 'wf_cnd_not_user_performs_action' AS item_name, 13802 AS sprite_id, 27629 AS offer_id
UNION ALL
    SELECT 'wf_cnd_slc_quantity' AS item_name, 14335 AS sprite_id, 28422 AS offer_id
UNION ALL
    SELECT 'wf_cnd_stuff_is' AS item_name, 5447 AS sprite_id, 13128 AS offer_id
UNION ALL
    SELECT 'wf_cnd_team_has_rank' AS item_name, 13811 AS sprite_id, 27638 AS offer_id
UNION ALL
    SELECT 'wf_cnd_team_has_score' AS item_name, 13812 AS sprite_id, 27639 AS offer_id
UNION ALL
    SELECT 'wf_cnd_time_less_than' AS item_name, 3682 AS sprite_id, 9117 AS offer_id
UNION ALL
    SELECT 'wf_cnd_time_more_than' AS item_name, 3665 AS sprite_id, 9100 AS offer_id
UNION ALL
    SELECT 'wf_cnd_trggrer_on_frn' AS item_name, 3694 AS sprite_id, 9129 AS offer_id
UNION ALL
    SELECT 'wf_cnd_triggerer_match' AS item_name, 13805 AS sprite_id, 27632 AS offer_id
UNION ALL
    SELECT 'wf_cnd_user_count_in' AS item_name, 5445 AS sprite_id, 13126 AS offer_id
UNION ALL
    SELECT 'wf_cnd_user_performs_action' AS item_name, 13809 AS sprite_id, 27636 AS offer_id
UNION ALL
    SELECT 'wf_cnd_valid_moves' AS item_name, 14553 AS sprite_id, 28807 AS offer_id
UNION ALL
    SELECT 'wf_slc_furni_altitude' AS item_name, 14330 AS sprite_id, 28417 AS offer_id
UNION ALL
    SELECT 'wf_slc_furni_area' AS item_name, 14333 AS sprite_id, 28420 AS offer_id
UNION ALL
    SELECT 'wf_slc_furni_bytype' AS item_name, 14334 AS sprite_id, 28421 AS offer_id
UNION ALL
    SELECT 'wf_slc_furni_neighborhood' AS item_name, 14342 AS sprite_id, 28429 AS offer_id
UNION ALL
    SELECT 'wf_slc_furni_onfurni' AS item_name, 14329 AS sprite_id, 28416 AS offer_id
UNION ALL
    SELECT 'wf_slc_furni_picks' AS item_name, 14337 AS sprite_id, 28424 AS offer_id
UNION ALL
    SELECT 'wf_slc_furni_signal' AS item_name, 14326 AS sprite_id, 28413 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_area' AS item_name, 14325 AS sprite_id, 28412 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_byaction' AS item_name, 14339 AS sprite_id, 28426 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_byname' AS item_name, 14327 AS sprite_id, 28414 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_bytype' AS item_name, 14343 AS sprite_id, 28430 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_group' AS item_name, 14331 AS sprite_id, 28418 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_handitem' AS item_name, 14336 AS sprite_id, 28423 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_neighborhood' AS item_name, 14346 AS sprite_id, 28433 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_onfurni' AS item_name, 14341 AS sprite_id, 28428 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_signal' AS item_name, 14338 AS sprite_id, 28425 AS offer_id
UNION ALL
    SELECT 'wf_slc_users_team' AS item_name, 14345 AS sprite_id, 28432 AS offer_id
UNION ALL
    SELECT 'wf_trg_at_given_time' AS item_name, 3679 AS sprite_id, 9114 AS offer_id
UNION ALL
    SELECT 'wf_trg_bot_reached_avtr' AS item_name, 7856 AS sprite_id, 16072 AS offer_id
UNION ALL
    SELECT 'wf_trg_bot_reached_stf' AS item_name, 7850 AS sprite_id, 16066 AS offer_id
UNION ALL
    SELECT 'wf_trg_click_furni' AS item_name, 14089 AS sprite_id, 28107 AS offer_id
UNION ALL
    SELECT 'wf_trg_click_tile' AS item_name, 14554 AS sprite_id, 28808 AS offer_id
UNION ALL
    SELECT 'wf_trg_click_user' AS item_name, 16878 AS sprite_id, 31820 AS offer_id
UNION ALL
    SELECT 'wf_trg_clock_counter' AS item_name, 13385 AS sprite_id, 26581 AS offer_id
UNION ALL
    SELECT 'wf_trg_collision' AS item_name, 5050 AS sprite_id, 12514 AS offer_id
UNION ALL
    SELECT 'wf_trg_enter_room' AS item_name, 3683 AS sprite_id, 9118 AS offer_id
UNION ALL
    SELECT 'wf_trg_game_ends' AS item_name, 3680 AS sprite_id, 9115 AS offer_id
UNION ALL
    SELECT 'wf_trg_game_starts' AS item_name, 3702 AS sprite_id, 9137 AS offer_id
UNION ALL
    SELECT 'wf_trg_period_long' AS item_name, 5042 AS sprite_id, 12506 AS offer_id
UNION ALL
    SELECT 'wf_trg_period_short' AS item_name, 14128 AS sprite_id, 28195 AS offer_id
UNION ALL
    SELECT 'wf_trg_periodically' AS item_name, 3671 AS sprite_id, 9106 AS offer_id
UNION ALL
    SELECT 'wf_trg_recv_signal' AS item_name, 14090 AS sprite_id, 28108 AS offer_id
UNION ALL
    SELECT 'wf_trg_says_something' AS item_name, 3675 AS sprite_id, 9110 AS offer_id
UNION ALL
    SELECT 'wf_trg_score_achieved' AS item_name, 3673 AS sprite_id, 9108 AS offer_id
UNION ALL
    SELECT 'wf_trg_state_changed' AS item_name, 3668 AS sprite_id, 9103 AS offer_id
UNION ALL
    SELECT 'wf_trg_stuff_state' AS item_name, 14091 AS sprite_id, 28109 AS offer_id
UNION ALL
    SELECT 'wf_trg_user_performs_action' AS item_name, 13807 AS sprite_id, 27634 AS offer_id
UNION ALL
    SELECT 'wf_trg_walks_off_furni' AS item_name, 3678 AS sprite_id, 9113 AS offer_id
UNION ALL
    SELECT 'wf_trg_walks_on_furni' AS item_name, 3703 AS sprite_id, 9138 AS offer_id
UNION ALL
    SELECT 'wf_var_echo' AS item_name, 16910 AS sprite_id, 31921 AS offer_id
UNION ALL
    SELECT 'wf_xtra_anim_time' AS item_name, 14131 AS sprite_id, 28198 AS offer_id
UNION ALL
    SELECT 'wf_xtra_execution_limit' AS item_name, 14098 AS sprite_id, 28116 AS offer_id
UNION ALL
    SELECT 'wf_xtra_filter_furni' AS item_name, 14344 AS sprite_id, 28431 AS offer_id
UNION ALL
    SELECT 'wf_xtra_filter_users' AS item_name, 14332 AS sprite_id, 28419 AS offer_id
UNION ALL
    SELECT 'wf_xtra_mov_carry_users' AS item_name, 14096 AS sprite_id, 28114 AS offer_id
UNION ALL
    SELECT 'wf_xtra_mov_curve' AS item_name, 18674 AS sprite_id, 34243 AS offer_id
UNION ALL
    SELECT 'wf_xtra_mov_no_animation' AS item_name, 14088 AS sprite_id, 28106 AS offer_id
UNION ALL
    SELECT 'wf_xtra_mov_physics' AS item_name, 14086 AS sprite_id, 28104 AS offer_id
UNION ALL
    SELECT 'wf_xtra_random' AS item_name, 3669 AS sprite_id, 9104 AS offer_id
UNION ALL
    SELECT 'wf_xtra_rotate_to_dir' AS item_name, 18675 AS sprite_id, 34244 AS offer_id
UNION ALL
    SELECT 'wf_xtra_text_output_furni_name' AS item_name, 18495 AS sprite_id, 33989 AS offer_id
UNION ALL
    SELECT 'wf_xtra_unseen' AS item_name, 3670 AS sprite_id, 9105 AS offer_id
UNION ALL
    SELECT 'wf_xtra_var_fx_boss' AS item_name, 19195 AS sprite_id, 34995 AS offer_id
UNION ALL
    SELECT 'wf_xtra_var_fx_health' AS item_name, 19196 AS sprite_id, 34996 AS offer_id
UNION ALL
    SELECT 'wf_xtra_var_fx_level' AS item_name, 19197 AS sprite_id, 34997 AS offer_id
UNION ALL
    SELECT 'wf_xtra_var_fx_number' AS item_name, 19198 AS sprite_id, 34998 AS offer_id
UNION ALL
    SELECT 'wf_xtra_var_fx_progress' AS item_name, 19199 AS sprite_id, 34999 AS offer_id
UNION ALL
    SELECT 'wf_xtra_var_fx_status' AS item_name, 19200 AS sprite_id, 35000 AS offer_id
UNION ALL
    SELECT 'wf_xtra_var_lvlup_system' AS item_name, 16909 AS sprite_id, 31920 AS offer_id
UNION ALL
    SELECT 'wf_xtra_var_time_util' AS item_name, 16908 AS sprite_id, 31919 AS offer_id
UNION ALL
    SELECT 'wf_upcounter1' AS item_name, 13383 AS sprite_id, 26579 AS offer_id
UNION ALL
    SELECT 'wf_upcounter2' AS item_name, 13396 AS sprite_id, 26592 AS offer_id
UNION ALL
    SELECT 'wf_game_upcounter1' AS item_name, 13395 AS sprite_id, 26591 AS offer_id
UNION ALL
    SELECT 'wf_game_upcounter2' AS item_name, 13384 AS sprite_id, 26580 AS offer_id
UNION ALL
    SELECT 'wf_antenna1' AS item_name, 14097 AS sprite_id, 28115 AS offer_id
UNION ALL
    SELECT 'wf_antenna2' AS item_name, 14095 AS sprite_id, 28113 AS offer_id
) AS official ON official.item_name = furni.item_name AND official.sprite_id = furni.sprite_id
LEFT JOIN catalog_items AS claimed ON claimed.offer_id = official.offer_id AND claimed.item_id <> item.item_id
SET item.offer_id = official.offer_id
WHERE item.offer_id <= 0 AND claimed.id IS NULL;
