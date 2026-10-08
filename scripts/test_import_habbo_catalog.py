"""pytest scripts/test_import_habbo_catalog.py — mapping and derivation rules of import-habbo-catalog.py."""
import importlib.util
import json
from pathlib import Path
import struct
import zlib

import pytest

SPEC = importlib.util.spec_from_file_location('import_habbo_catalog', Path(__file__).with_name('import-habbo-catalog.py'))
m = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(m)


def hab(name, logic, states=0, height=1.0, visualization='furniture_animated'):
    document = {'name': name, 'logicType': logic, 'visualizationType': visualization,
                'logic': {'model': {'dimensions': {'x': 1, 'y': 1, 'z': height}}},
                'visualizations': [{'size': 64, 'animations': {str(n): {} for n in range(states)} | {'1001': {}}}]}
    body = json.dumps(document).encode()
    stored = zlib.compress(body)
    index = zlib.compress(json.dumps({'format': 'hab', 'version': 1, 'name': name, 'entries': [
        {'name': name + '.json', 'mimeType': 'application/json', 'offset': 0, 'storedLength': len(stored),
         'originalLength': len(body), 'compression': 'deflate'}]}).encode())
    return b'HAB\0' + struct.pack('<HHIII', 1, 1, len(index), 0, len(stored)) + index + stored


def entry(classname, id=1, **fields):
    return {'id': id, 'classname': classname, 'revision': 1, 'category': 'other', 'defaultdir': 0, 'xdim': 1, 'ydim': 1,
            'name': classname, 'description': '', 'adurl': None, 'offerid': -1, 'excludeddynamic': False, 'customparams': None,
            'specialtype': 1, 'canstandon': False, 'cansiton': False, 'canlayon': False, 'canputstuffon': True, 'height': 1.0,
            'furniline': 'line', 'environment': None, 'rare': False, 'tradeable': True, 'recyclable': True, **fields}


def derive(classname, kind='s', logic=None, vote=None, visualization='furniture_animated', **fields):
    info = {'logicType': logic, 'visualizationType': visualization, 'height': 1, 'states': 2} if logic else None
    return m.derive_interaction(kind, entry(classname, **fields), info, vote, {'wf_trg_says_something'})


# ---- HAB and names ---------------------------------------------------------

def test_hab_logic_reads_logic_type_height_and_states_without_transitions():
    assert m.hab_logic(hab('lamp', 'furniture_multistate', states=3, height=1.5)) == {
        'logicType': 'furniture_multistate', 'visualizationType': 'furniture_animated', 'height': 1.5, 'states': 3}


def test_read_hab_rejects_other_files():
    with pytest.raises(ValueError):
        m.read_hab(b'NITRO' + bytes(30))


def test_library_and_icon_names_follow_the_client_templates():
    assert m.library('table_plasto_square*9') == 'table_plasto_square'
    assert m.icon_name('table_plasto_square*9') == 'table_plasto_square_9_icon.png'
    assert m.icon_name('chair') == 'chair_icon.png'


def test_every_derivable_interaction_is_parsed_by_the_emulator():
    assert m.derivable_interactions() <= m.interaction_names()
    assert 'wf_trg_says_something' in m.wired_box_names()


# ---- furnidata columns -----------------------------------------------------

def test_furnidata_columns_store_entries_as_migration_60():
    columns = m.furnidata_columns(entry('chair', partcolors={'color': ['#fff', '0']}, adurl=None, canputstuffon=None))
    assert columns['part_colors'] == '#fff,0'
    assert columns['ad_url'] is None and columns['can_put_stuff_on'] is None
    assert columns['excluded_dynamic'] == 0 and columns['tradeable'] == 1
    assert m.furnidata_columns(entry('chair'))['part_colors'] is None
    assert m.furnidata_columns({'id': 1, 'classname': 'poster', 'partcolors': {'color': []}})['part_colors'] == ''
    wall = m.furnidata_columns({'id': 4001, 'classname': 'poster'})
    assert (wall['default_dir'], wall['xdim'], wall['special_type'], wall['can_stand_on']) == (0, 1, 1, 0)


def test_clothing_parts_normalise_habbo_customparams():
    assert m.clothing_parts({'customparams': '3593, 3594,'}) == '3593,3594'
    assert m.clothing_parts({'customparams': ''}) is None
    assert m.clothing_parts({'customparams': '1,x'}) is None


# ---- interaction derivation ------------------------------------------------

@pytest.mark.parametrize('classname, kind, fields, logic, expected', [
    ('wallpaper', 'i', {}, None, ('wallpaper', 'furnidata:classname')),
    ('post_it', 'i', {'specialtype': 5}, 'furniture_stickie', ('postit', 'furnidata:specialtype=5')),
    ('song_disk', 's', {'specialtype': 8}, None, ('musicdisc', 'furnidata:specialtype=8')),
    ('clothing_bow', 's', {'specialtype': 23}, 'furniture_purchasable_clothing', ('purchasable_clothing', 'furnidata:specialtype=23')),
    ('horse_saddle2', 's', {'specialtype': 16}, None, ('horse_saddle_2', 'furnidata:specialtype=16')),
    ('gld_gate', 's', {'specialtype': 17, 'category': 'gate'}, None, ('gld_gate', 'furnidata:specialtype=17')),
    ('guild_forum', 's', {'specialtype': 17}, 'furniture_group_forum_terminal', ('guild_forum', 'furnidata:specialtype=17,hab:logicType')),
    ('bed_polyfon', 's', {'canlayon': True}, 'furniture_basic', ('bed', 'furnidata:canlayon')),
    ('one_way_door*1', 's', {'category': 'gate'}, 'furniture_one_way_door', ('onewaygate', 'hab:logicType=furniture_one_way_door')),
    ('fball_ball', 's', {}, 'furniture_pushable', ('ball', 'hab:logicType=furniture_pushable')),
    ('bb_puck', 's', {}, 'furniture_pushable', ('banzaipuck', 'hab:logicType=furniture_pushable')),
    ('edice', 's', {}, 'furniture_dice', ('dice', 'hab:logicType=furniture_dice')),
    ('tent_blue', 's', {'category': 'tent'}, 'furniture_change_state_when_step_on', ('tent', 'hab:logicType=furniture_change_state_when_step_on')),
    ('door', 's', {'category': 'teleport'}, 'furniture_multistate', ('teleport', 'category=teleport')),
    ('divider_nor3', 's', {'category': 'gate'}, None, ('gate', 'category=gate')),
    ('sf_roller', 's', {'category': 'roller'}, 'furniture_basic', ('roller', 'category=roller')),
    ('wf_trg_says_something', 's', {'category': 'wired'}, 'furniture_multistate', ('wf_trg_says_something', 'classname:wired')),
    ('fball_goal_b', 's', {'category': 'games'}, 'furniture_multistate', ('blue_goal', 'classname')),
    ('es_score_y', 's', {'category': 'games'}, 'furniture_score', ('freezeyellowcounter', 'classname')),
    ('tile_stackmagic2', 's', {}, 'furniture_custom_stack_height', ('stacktool', 'classname')),
    ('chair_polyfon', 's', {'category': 'chair'}, 'furniture_basic', (None, None)),
    ('lamp_basic', 's', {'category': 'lighting'}, 'furniture_multistate', (None, None)),
])
def test_derivation_layers(classname, kind, fields, logic, expected):
    visualization = 'furniture_queue_tile' if classname == 'sf_roller' else 'furniture_animated'
    assert derive(classname, kind, logic, visualization=visualization, **fields) == expected


def test_bottle_is_picked_by_visualization_or_classname():
    assert derive('hween10_tarot', logic='furniture_dice', visualization='furniture_bottle')[0] == 'bottle'
    assert derive('bottle', logic='furniture_dice', category='fortuna')[0] == 'bottle'


def test_a_gate_category_needs_a_multistate_bundle():
    assert derive('divider_nor3', logic='furniture_basic', category='gate') == (None, None)


def test_a_vending_machine_needs_hand_items_from_a_vote():
    assert derive('bar_polyfon', logic='furniture_multistate', category='vending_machine') == (None, None)
    vote = {'interaction': 'vendingmachine', 'modes': 0, 'vending_ids': '6,5,2', 'multiheight': ''}
    assert derive('bar_polyfon', logic='furniture_multistate', category='vending_machine', vote=vote) == ('vendingmachine', 'category=vending_machine')


def test_votes_count_only_when_the_bundle_logic_agrees():
    gate = {'interaction': 'gate', 'modes': 2, 'vending_ids': '0', 'multiheight': ''}
    assert derive('xmas_gate', logic='furniture_multistate', vote=gate) == ('gate', 'vote:arcturus=gate')
    assert derive('xmas_gate', logic='furniture_basic', vote=gate) == (None, None)
    assert derive('xmas_gate', logic=None, vote=gate) == (None, None)


def test_behaviour_columns_follow_furnidata_and_the_bundle():
    vote = {'interaction': 'multiheight', 'modes': 3, 'vending_ids': '0', 'multiheight': '0.5;1;1.5'}
    info = {'logicType': 'furniture_multiheight', 'visualizationType': 'furniture_animated', 'height': 0.5, 'states': 3}
    columns = m.behaviour_columns('s', entry('school_platform', xdim=2, height=0.5, cansiton=True), info, vote, None)
    assert columns == {'width': 2, 'length': 1, 'can_sit': 1, 'is_walkable': 0, 'stack_height': 0.5,
                       'interaction_modes_count': 3, 'height_adjustable': '0.5,1,1.5'}
    assert m.behaviour_columns('s', entry('table', canputstuffon=False), None, None, None, stacking=True)['can_stack'] == 0
    mismatch = dict(info, states=2)
    assert 'height_adjustable' not in m.behaviour_columns('s', entry('x'), mismatch, vote, None)


def test_hint_names_what_is_not_decorative():
    assert m.hint('s', entry('chair', category='chair'), {'logicType': 'furniture_basic'}, None) is None
    assert m.hint('s', entry('snowball', category='games', specialtype=18), {'logicType': 'furniture_snowball'}, None) == \
        'logicType=furniture_snowball, category=games, specialtype=18'


def test_arcturus_votes_parse_items_base_rows(tmp_path):
    sql = tmp_path / 'catalog.sql'
    sql.write_text("INSERT INTO `items_base` VALUES (127, 127, 'bar_polyfon', 'Mini-bar', 's', 1, 1, 1.00, 1, 0, 0, 0, 1, 1, 0, 0, 1, "
                   "'vendingmachine', 0, '6,5,2,1', '', '', 0, 0, '', '17', '0');\n"
                   "INSERT INTO `items_base` VALUES (3263, 3263, 'ktchn_plates', 'Dinner \\'Plates\\'', 's', 1, 1, 0.20, 1, 0, 0, 0, 1, 1, 0, 0, 1, "
                   "'multiheight', 3, '0', '0.2;0.5;0.9', '', 0, 0, '', '59', '0');\n", encoding='latin-1')
    votes = m.arcturus_votes(sql)
    assert votes[('s', 'bar_polyfon')] == {'interaction': 'vendingmachine', 'modes': 0, 'vending_ids': '6,5,2,1', 'multiheight': ''}
    assert votes[('s', 'ktchn_plates')]['multiheight'] == '0.2;0.5;0.9'


# ---- furniture plan --------------------------------------------------------

def row(id, name, sprite, kind='s', owner=1, interaction='default', **columns):
    base = {column: None for column in m.FURNITURE_COLUMNS}
    base.update(id=id, item_name=name, public_name=name, type=kind, width=1, length=1, stack_height=0.0, can_stack=1, can_sit=0,
                is_walkable=0, sprite_id=sprite, allow_recycle=1, allow_trade=1, allow_marketplace_sell=1, allow_gift=1,
                allow_inventory_stack=1, interaction_type=interaction, interaction_modes_count=1, vending_ids='0',
                height_adjustable='0', is_rare=0, clothing_id=0, has_furnidata=owner)
    base.update(columns)
    return base


def apply_furniture(furniture, result):
    """What apply writes, done to the snapshot rows."""
    by_id = {r['id']: r for r in furniture}
    for row_id, columns in result['updates'].items():
        by_id[row_id].update(columns)
    next_id = max(by_id) + 1
    for insert in result['inserts']:
        furniture.append(dict(insert, id=next_id, clothing_id=insert['clothing_id'] if not isinstance(insert['clothing_id'], tuple) else 7))
        next_id += 1
    for r in furniture:
        if isinstance(r.get('clothing_id'), tuple):
            r['clothing_id'] = 7


def furniture_case():
    habbo = {('s', 'chair'): entry('chair', id=10, category='chair'), ('s', 'gate_x'): entry('gate_x', id=20, category='gate'),
             ('s', 'Lamp'): entry('Lamp', id=30), ('s', 'new_sofa'): entry('new_sofa', id=40, cansiton=True, height=0.8),
             ('s', 'pirate_teleport'): entry('pirate_teleport', id=50), ('s', 'clothing_bow'): entry('clothing_bow', id=60, specialtype=23, customparams='3141,')}
    furniture = [
        row(1, 'chair', 20),                      # moves to Habbo's 10
        row(2, 'gate_x', 10, interaction='gate'), # moves to 20, keeps its own interaction
        row(3, 'custom_rug', 40),                 # Plus-only furni holding Habbo's 40 moves above every id
        row(4, 'lamp', 30),                       # classname case follows Habbo
        row(5, 'chair', 20, owner=0, interaction='bottle'),  # Plus duplicate follows its owner's sprite
        row(6, 'pirate_teleport\r\n', 99, owner=0),          # line-break junk takes the free entry
        row(7, 'clothing_bow', 60),
    ]
    return habbo, furniture


def test_plan_furniture_moves_sprites_resolves_collisions_and_creates_sold_furni():
    habbo, furniture = furniture_case()
    evidence = m.Evidence({'gate_x': {'logicType': 'furniture_multistate', 'visualizationType': 'furniture_animated', 'height': 1, 'states': 2}})
    result = m.plan_furniture(furniture, habbo, {('s', 'new_sofa')}, evidence, {})
    updates, report = result['updates'], result['report']
    assert updates[1]['sprite_id'] == 10 and updates[1]['has_furnidata'] == 1
    assert updates[2]['sprite_id'] == 20 and 'interaction_type' not in updates[2]
    assert updates[3]['sprite_id'] == 100 and report['sprite_collisions'][0]['habbo_classname'] == 'new_sofa'
    assert updates[4]['item_name'] == 'Lamp' and updates[5]['sprite_id'] == 10
    assert {k: updates[6][k] for k in ('item_name', 'has_furnidata', 'sprite_id')} == {'item_name': 'pirate_teleport', 'has_furnidata': 1, 'sprite_id': 50}
    assert updates[7]['interaction_type'] == 'purchasable_clothing' and updates[7]['clothing_id'] == ('clothing', 'clothing_bow')
    assert [(r['item_name'], r['sprite_id'], r['can_sit'], r['stack_height']) for r in result['inserts']] == [('new_sofa', 40, 1, 0.8)]
    assert result['owner_of'][('s', 'new_sofa')] == ('new', 's', 'new_sofa')
    assert {r['reason'] for r in report['renamed']} == {'case', 'whitespace'}


def test_plan_furniture_is_a_no_op_after_its_own_changes():
    habbo, furniture = furniture_case()
    evidence = m.Evidence()
    apply_furniture(furniture, m.plan_furniture(furniture, habbo, {('s', 'new_sofa')}, evidence, {}))
    again = m.plan_furniture(furniture, habbo, {('s', 'new_sofa')}, evidence, {'clothing_bow': (7, '3141')})
    assert again['updates'] == {} and again['inserts'] == []


def test_furniture_statements_free_unique_keys_before_ids_move():
    habbo, furniture = furniture_case()
    result = {'furniture': m.plan_furniture(furniture, habbo, set(), m.Evidence(), {}), 'clothing': [], 'badges': [], 'promotions': None,
              'tables': {table: {'insert': [], 'update': [], 'delete': []} for table in m.CATALOG_TABLES}}
    sql = m.statements(result)
    free = next(i for i, s in enumerate(sql) if s.startswith('UPDATE `furniture` SET `has_furnidata` = FALSE'))
    first_move = next(i for i, s in enumerate(sql) if '`sprite_id` = ' in s)
    assert free < first_move
    assert all('`has_furnidata` = 1' in s for s in sql if s.startswith('UPDATE `furniture` SET') and '`sprite_id` = 10' in s and 'WHERE `id` = 1;' in s)


# ---- catalogue plan --------------------------------------------------------

def offer(offer_id, class_id, **fields):
    product = {'productType': 's', 'furniClassId': class_id, 'extraParam': '', 'productCount': 1, 'uniqueLimitedItem': False,
               'uniqueLimitedItemSeriesSize': 0, 'uniqueLimitedItemsLeft': 0}
    base = {'offerId': offer_id, 'localizationId': f'offer_{offer_id}', 'rent': False, 'priceInCredits': 3, 'priceInActivityPoints': 0,
            'activityPointType': 0, 'priceInSilver': 0, 'giftable': True, 'products': [product], 'clubLevel': 0,
            'bundlePurchaseAllowed': True, 'isPet': False, 'previewImage': ''}
    base.update(fields)
    return base


def page(page_id, offers, layout='default_3x3', front=()):
    return {'pageId': page_id, 'catalogType': 'NORMAL', 'layoutCode': layout, 'images': ['header', ''], 'texts': ['text'],
            'offers': offers, 'offerId': -1, 'acceptSeasonCurrencyAsCredits': False, 'frontPageItems': list(front)}


def node(page_id, name, children=()):
    return {'visible': True, 'icon': 1, 'pageId': page_id, 'pageName': name, 'localization': name.title(), 'offerIds': [], 'children': list(children)}


def snapshot(**tables):
    base = {table: [] for table in m.SNAPSHOT}
    base.update(tables)
    return base


def test_plan_catalog_builds_pages_offers_and_reports_what_it_cannot_keep():
    ltd = offer(30, 10, products=[dict(offer(0, 10)['products'][0], uniqueLimitedItem=True, uniqueLimitedItemSeriesSize=100, uniqueLimitedItemsLeft=0)])
    catalog = {'index': node(-1, 'root', [node(1, 'front'), node(-1, 'folder', [node(2, 'chairs'), node(2, 'chairs_again'), node(3, 'Chairs')])]),
               'pages': {'1': page(1, [], 'frontpage4', [{'position': 0, 'itemName': 'Chairs', 'itemPromoImage': 'x.png', 'type': 0,
                                                          'cataloguePageLocation': 'chairs', 'productOfferId': None, 'productCode': None,
                                                          'secondsToExpiration': 60}]),
                         '2': page(2, [offer(10, 10, priceInActivityPoints=5, activityPointType=103), ltd, offer(31, 10, rent=True), offer(32, 999)]),
                         '3': page(3, [offer(10, 10, priceInActivityPoints=5, activityPointType=103)])},
               'source': {'capturedAt': '2026-10-08T00:00:00Z'}}
    current = snapshot(catalog_offer_limited=[{'offer_id': 30, 'stack': 100, 'sold': 7}],
                       club_gift_offers=[{'offer_id': 77}], catalog_offers=[dict(id=77, localization_key='gift', cost_credits=0, cost_points=0,
                                                                                 points_type=0, club_level=0, bulk_purchase=1, enabled=1, preview_image='')],
                       catalog_offer_products=[dict(offer_id=77, position=0, product_type='furni', furniture_id=5, effect_id=None, badge_code=None,
                                                    bot_preset_id=None, pet_type=None, habbicon_id=None, amount=1, extra_param='')])
    result = m.plan_catalog(catalog, {('s', 10): 'chair'}, lambda kind, name: 5 if name == 'chair' else None, current)
    tables, report = result['tables'], result['report']
    pages = {key[0]: value for key, value in tables['catalog_pages'].items()}
    folder = m.FOLDER_PAGE_ID_BASE + 1
    assert pages[folder]['enabled'] == 0 and pages[2]['parent_id'] == folder and pages[1]['parent_id'] is None
    copy = pages[m.FOLDER_PAGE_ID_BASE + 2]
    assert copy['enabled'] == 1 and copy['link'] == 'chairs_again' and (copy['id'], 10) in tables['catalog_page_offers']
    assert pages[3]['link'] is None and report['duplicate_links'] == [{'id': 3, 'link': 'Chairs'}]
    assert tables['catalog_offers'][(10,)]['points_type'] == 103 and tables['catalog_offers'][(10,)]['cost_points'] == 5
    assert tables['catalog_offer_limited'][(30,)] == {'offer_id': 30, 'stack': 100, 'sold': 7}
    assert {d['offerId']: d['reason'] for d in report['dropped_offers']} == {31: 'rent offer', 32: 'furni s:999 (not in furnidata) has no definition'}
    # The club gift's offer stays with its product, off every page.
    assert (77,) in tables['catalog_offers'] and (77, 0) in tables['catalog_offer_products'] and report['kept_offers'] == [77]
    assert result['promotions'][0]['page_link'] == 'chairs' and result['promotions'][0]['expires_at'] == '2026-10-08 00:01:00.000000'


def test_missing_pages_stop_the_plan_unless_allowed():
    catalog = {'index': node(-1, 'root', [node(5, 'gone')]), 'pages': {}}
    with pytest.raises(ValueError):
        m.plan_catalog(catalog, {}, lambda kind, name: None, snapshot())
    result = m.plan_catalog(catalog, {}, lambda kind, name: None, snapshot(), allow_missing_pages=True)
    assert result['tables']['catalog_pages'][(5,)]['enabled'] == 0


def test_page_statements_release_links_before_reuse():
    current = [dict(id=1, parent_id=None, link='a', caption='A', layout='default_3x3', required_permission=None, visible=1, enabled=1,
                    icon=1, required_club_level=0, position=0),
               dict(id=2, parent_id=None, link='b', caption='B', layout='default_3x3', required_permission=None, visible=1, enabled=1,
                    icon=1, required_club_level=0, position=1)]
    desired = {(1,): dict(current[0], link='b')}
    tables = {table: {'insert': [], 'update': [], 'delete': []} for table in m.CATALOG_TABLES}
    tables['catalog_pages'] = m.diff_table('catalog_pages', current, desired)
    sql = m.statements({'furniture': {'updates': {}, 'inserts': []}, 'clothing': [], 'badges': [], 'promotions': None, 'tables': tables})
    release = sql.index('UPDATE `catalog_pages` SET `link` = NULL WHERE `id` = 2;')
    take = next(i for i, s in enumerate(sql) if s.startswith('UPDATE `catalog_pages` SET `link` = CONVERT'))
    assert release < take < sql.index('DELETE FROM `catalog_pages` WHERE `id` = 2;')


def test_literals_never_inline_text():
    assert m.literal("x'; DROP TABLE furniture; --").startswith('CONVERT(0x')
    assert m.literal(('new', 's', 'chair')).startswith('(SELECT `id` FROM `furniture`')
    assert m.literal(None) == 'NULL' and m.literal(True) == '1' and m.literal(0.5) == '0.5'


def test_links_compare_without_case_or_accents():
    assert m.link_key('Café_Set') == m.link_key('cafe_set')


def test_a_classname_the_other_kind_owns_is_not_created():
    habbo = {('s', 'shared'): entry('shared', id=5)}
    result = m.plan_furniture([row(1, 'Shared', 7, kind='i')], habbo, {('s', 'shared')}, m.Evidence(), {})
    assert result['inserts'] == [] and 'shared' in {c['classname'] for c in result['report']['kind_conflicts']}
