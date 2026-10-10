"""pytest scripts/test_import_habbo_catalog.py — mapping and derivation rules of import-habbo-catalog.py."""
import importlib.util
import json
import csv
import sqlite3
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
    assert m.derivable_interactions() <= m.interaction_names() | m.wired_box_names().keys()
    assert 'wf_trg_says_something' in m.wired_box_names()


WIRED_FURNITURE_INTERACTIONS = [
    ('wf_xtra_varfx_hp', 'wf_xtra_var_fx_health'),
    ('wf_xtra_varfx_prog', 'wf_xtra_var_fx_progress'),
    ('wf_xtra_varfx_levelling', 'wf_xtra_var_fx_level'),
    ('wf_xtra_varfx_status', 'wf_xtra_var_fx_status'),
    ('wf_xtra_varfx_boss', 'wf_xtra_var_fx_boss'),
    ('wf_xtra_varfx_number', 'wf_xtra_var_fx_number'),
    ('wf_proto_trg_at_given_time', 'wf_trg_at_given_time'),
    ('wf_proto_cnd_trggrer_on_frn', 'wf_cnd_trggrer_on_frn'),
    ('wf_ltdproto_act_toggle_state', 'wf_act_toggle_state'),
]


@pytest.mark.parametrize('classname,interaction', WIRED_FURNITURE_INTERACTIONS)
def test_official_wired_import_stores_a_concrete_database_interaction(classname, interaction):
    wired = m.wired_box_names()
    assert classname not in wired
    assert interaction in wired
    assert m.derive_interaction('s', entry(classname), None, None, wired) == (interaction, 'classname')


@pytest.mark.parametrize('classname,interaction', WIRED_FURNITURE_INTERACTIONS)
def test_official_wired_database_repair_and_new_import_are_idempotent(classname, interaction):
    evidence = m.Evidence(wired=m.wired_box_names())
    habbo = {('s', classname): entry(classname, id=123)}
    furniture = [row(7, classname, 123, interaction='default', interaction_modes_count=3)]
    before = furniture[0].copy()
    repair = m.plan_furniture(furniture, habbo, set(), evidence, {})
    assert repair['updates'][7]['interaction_type'] == interaction
    assert 'interaction_modes_count' not in repair['updates'][7]
    apply_furniture(furniture, repair)
    assert (furniture[0]['id'], furniture[0]['item_name'], furniture[0]['sprite_id']) == (7, classname, 123)
    assert furniture[0]['interaction_modes_count'] == before['interaction_modes_count']
    repeat = m.plan_furniture(furniture, habbo, set(), evidence, {})
    assert not repeat['updates'] and not repeat['inserts']
    created = m.plan_furniture([], habbo, {('s', classname)}, evidence, {})
    assert created['inserts'][0]['interaction_type'] == interaction
    assert created['inserts'][0]['item_name'] == classname


def test_wired_migration_repairs_only_known_definitions_and_keeps_saved_configuration():
    root = Path(__file__).resolve().parents[1]
    fresh = (root / 'Database/FreshInstall.sql').read_text()
    migration = (root / 'Database/Migrations/64_WiredFurnitureInteractions.sql').read_text()
    database = sqlite3.connect(':memory:')
    database.execute('CREATE TABLE furniture (id INTEGER PRIMARY KEY, item_name TEXT, type TEXT, sprite_id INTEGER, interaction_type TEXT)')
    database.execute('CREATE TABLE wired_item_configurations (item_id INTEGER, box_name TEXT, configuration TEXT)')
    expected = {}
    for index, (classname, interaction) in enumerate(WIRED_FURNITURE_INTERACTIONS):
        line = next(line for line in fresh.splitlines()
                    if line.startswith('(') and ",'%s'," % classname in line and ",'s'," in line)
        values = next(csv.reader([line[1:].rstrip(',;')[:-1]], quotechar="'", escapechar='\\'))
        # Fresh installs must already contain the explicit interaction; no runtime name rewrite is involved.
        assert values[1] == classname and values[16] == interaction
        sprite = int(values[10])
        generic = m.wired_box_names()[interaction]
        for offset, (name, kind, graphic, old, new) in enumerate([
            (classname, 's', sprite, 'default', interaction),
            (classname, 's', sprite, generic, interaction),
            (classname, 's', sprite, classname, interaction),
            (classname, 's', sprite, interaction, interaction),
            (classname, 's', sprite, 'gate', 'gate'),
            (classname, 's', sprite + 1, 'default', 'default'),
            ('custom_' + classname, 's', sprite, 'default', 'default'),
            (classname, 'i', sprite, 'default', 'default'),
        ]):
            if len(old) > 25:  # The production column cannot store the long Ancient asset names.
                continue
            item_id = index * 10 + offset
            database.execute('INSERT INTO furniture VALUES (?,?,?,?,?)', (item_id, name, kind, graphic, old))
            expected[item_id] = new
        database.execute('INSERT INTO wired_item_configurations VALUES (?,?,?)', (index, interaction, '{"IntParams":[1,2],"Text":"saved"}'))
    identities = database.execute('SELECT id,item_name,type,sprite_id FROM furniture ORDER BY id').fetchall()
    saved = database.execute('SELECT * FROM wired_item_configurations').fetchall()
    before = database.total_changes
    database.executescript(migration)
    assert database.total_changes - before == 24
    assert dict(database.execute('SELECT id,interaction_type FROM furniture')) == expected
    assert database.execute('SELECT id,item_name,type,sprite_id FROM furniture ORDER BY id').fetchall() == identities
    assert database.execute('SELECT * FROM wired_item_configurations').fetchall() == saved
    before = database.total_changes
    database.executescript(migration)
    assert database.total_changes == before
    database.close()


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


def test_behaviour_columns_follow_furnidata_and_the_bundle():
    vote = {'interaction': 'multiheight', 'modes': 3, 'vending_ids': '0', 'multiheight': '0.5;1;1.5'}
    info = {'logicType': 'furniture_multiheight', 'visualizationType': 'furniture_animated', 'height': 0.5, 'states': 3}
    columns = m.behaviour_columns('s', entry('school_platform', xdim=2, height=0.5, cansiton=True), info, vote, None)
    assert columns == {'width': 2, 'length': 1, 'can_sit': 1, 'is_walkable': 0, 'stack_height': 0.5, 'can_stack': 1,
                       'height_adjustable': '0.5,1,1.5'}
    assert m.physical_columns('s', entry('table', canputstuffon=False), None)['can_stack'] == 0
    mismatch = dict(info, states=2)
    assert 'height_adjustable' not in m.behaviour_columns('s', entry('x'), mismatch, vote, None)


def test_hint_names_what_is_not_decorative():
    assert m.hint('s', entry('chair', category='chair'), {'logicType': 'furniture_basic'}, None) is None
    assert m.hint('s', entry('snowball', category='games', specialtype=18), {'logicType': 'furniture_snowball'}, None) == \
        'logicType=furniture_snowball, category=games, specialtype=18'


def row(id, name, sprite, kind='s', owner=1, interaction='default', **columns):
    base = {column: None for column in m.FURNITURE_COLUMNS}
    base.update(id=id, item_name=name, public_name=name, type=kind, width=1, length=1, stack_height=0.0, can_stack=1, can_sit=0,
                is_walkable=0, sprite_id=sprite, allow_recycle=1, allow_trade=1, allow_marketplace_sell=1, allow_gift=1,
                allow_inventory_stack=1, interaction_type=interaction, interaction_modes_count=1, vending_ids='0',
                height_adjustable='0', is_rare=0, has_furnidata=owner)
    base.update(columns)
    return base


def apply_furniture(furniture, result):
    """What apply writes, done to the snapshot rows."""
    by_id = {r['id']: r for r in furniture}
    for row_id, columns in result['updates'].items():
        by_id[row_id].update(columns)
    next_id = max(by_id) + 1
    for insert in result['inserts']:
        furniture.append(dict(insert, id=next_id))
        next_id += 1


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
    result = m.plan_furniture(furniture, habbo, {('s', 'new_sofa')}, evidence)
    updates, report = result['updates'], result['report']
    assert updates[1]['sprite_id'] == 10 and updates[1]['has_furnidata'] == 1
    assert updates[2]['sprite_id'] == 20 and 'interaction_type' not in updates[2]
    assert updates[3]['sprite_id'] == 100 and report['sprite_collisions'][0]['habbo_classname'] == 'new_sofa'
    assert updates[4]['item_name'] == 'Lamp' and updates[5]['sprite_id'] == 10
    assert {k: updates[6][k] for k in ('item_name', 'has_furnidata', 'sprite_id')} == {'item_name': 'pirate_teleport', 'has_furnidata': 1, 'sprite_id': 50}
    assert updates[7]['interaction_type'] == 'purchasable_clothing'
    assert [(r['item_name'], r['sprite_id'], r['can_sit'], r['stack_height']) for r in result['inserts']] == [('new_sofa', 40, 1, 0.8)]
    assert result['owner_of'][('s', 'new_sofa')] == ('new', 's', 'new_sofa')
    assert {r['reason'] for r in report['renamed']} == {'case', 'whitespace'}


def test_plan_furniture_is_a_no_op_after_its_own_changes():
    habbo, furniture = furniture_case()
    evidence = m.Evidence()
    apply_furniture(furniture, m.plan_furniture(furniture, habbo, {('s', 'new_sofa')}, evidence))
    again = m.plan_furniture(furniture, habbo, {('s', 'new_sofa')}, evidence)
    assert again['updates'] == {} and again['inserts'] == []


def test_furniture_statements_free_unique_keys_before_ids_move():
    habbo, furniture = furniture_case()
    result = {'furniture': m.plan_furniture(furniture, habbo, set(), m.Evidence()), 'badges': [], 'promotions': None,
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
                       club_gift_offers=[{'offer_id': 77, 'days_required': 0, 'enabled': 1}], catalog_offers=[dict(id=77, localization_key='gift', cost_credits=0, cost_points=0,
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
    assert (77,) in tables['club_gift_offers'] and report['club_gifts'] == {'captured': False}
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
    sql = m.statements({'furniture': {'updates': {}, 'inserts': []}, 'badges': [], 'promotions': None, 'tables': tables})
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
    result = m.plan_furniture([row(1, 'Shared', 7, kind='i')], habbo, {('s', 'shared')}, m.Evidence())
    assert result['inserts'] == [] and 'shared' in {c['classname'] for c in result['report']['kind_conflicts']}


def test_physical_columns_apply_to_owners_whatever_their_interaction():
    habbo = {('s', 'gate_x'): entry('gate_x', id=20, xdim=2, height=0.4, canputstuffon=False)}
    hab_info = {'gate_x': {'logicType': 'furniture_multistate', 'visualizationType': 'furniture_animated', 'height': 0.4, 'states': 3}}
    result = m.plan_furniture([row(1, 'gate_x', 20, interaction='gate')], habbo, set(), m.Evidence(hab_info))
    assert {k: result['updates'][1][k] for k in ('width', 'stack_height', 'can_stack', 'interaction_modes_count')} == \
        {'width': 2, 'stack_height': 0.4, 'can_stack': 0, 'interaction_modes_count': 3}
    assert 'interaction_type' not in result['updates'][1] and result['report']['stacking_turned_off'] == 1


def test_captured_club_gifts_replace_the_configured_ones_on_a_hidden_page():
    catalog = {'index': node(-1, 'root', [node(1, 'front')]), 'pages': {'1': page(1, [])},
               'clubGifts': {'daysUntilNextGift': 3, 'giftsAvailable': 1, 'offers': [offer(500, 10, priceInCredits=0), offer(501, 999)],
                             'giftData': [{'offerId': 500, 'isVip': True, 'daysRequired': 31, 'isSelectable': True},
                                          {'offerId': 501, 'isVip': False, 'daysRequired': 0, 'isSelectable': True}]}}
    current = snapshot(club_gift_offers=[{'offer_id': 77, 'days_required': 0, 'enabled': 1}])
    result = m.plan_catalog(catalog, {('s', 10): 'chair'}, lambda kind, name: 5 if name == 'chair' else None, current)
    tables, report = result['tables'], result['report']
    hidden = tables['catalog_pages'][(m.CLUB_GIFT_PAGE_ID,)]
    assert (hidden['visible'], hidden['enabled'], hidden['link']) == (0, 1, None)
    assert (m.CLUB_GIFT_PAGE_ID, 500) in tables['catalog_page_offers']
    assert tables['club_gift_offers'] == {(500,): {'offer_id': 500, 'days_required': 31, 'enabled': 1}}
    assert report['club_gifts'] == {'captured': True, 'offers': 1, 'gifts': 1, 'vip': 1, 'without_offer': [501]}
    assert m.diff_table('club_gift_offers', current['club_gift_offers'], tables['club_gift_offers'])['delete'] == [(77,)]


def test_club_gift_furni_count_as_sold():
    catalog = {'pages': {'1': page(1, [offer(1, 10)])}, 'clubGifts': {'offers': [offer(2, 20, productType='s')], 'giftData': []}}
    assert set(m.capture_classnames(catalog)) == {('s', 10), ('s', 20)}


def test_minus_one_activity_points_mean_none():
    catalog = {'index': node(-1, 'root', [node(1, 'wired')]), 'pages': {'1': page(1, [offer(9, 10, priceInActivityPoints=-1)])}}
    result = m.plan_catalog(catalog, {('s', 10): 'chair'}, lambda kind, name: 5, snapshot())
    assert result['tables']['catalog_offers'][(9,)]['cost_points'] == 0 and result['report']['ignored']['points_minus_one'] == 1


def test_pet_offers_sell_the_pet_type_named_by_their_localization():
    catalog = {'index': node(-1, 'root', [node(1, 'pets')]), 'pages': {'1': page(1, [offer(11060, 4506, localizationId='a0 pet20')], 'pets')}}
    result = m.plan_catalog(catalog, {}, lambda kind, name: None, snapshot())
    product = result['tables']['catalog_offer_products'][(11060, 0)]
    assert (product['product_type'], product['pet_type'], product['furniture_id']) == ('pet', 20, None)


# ---- Builders Club merge ---------------------------------------------------

def builders_club_case():
    normal = {'index': node(-1, 'root', [node(1, 'set_anna', [node(2, 'anna_sub')]), node(9, 'bc_frontpage')]),
              'pages': {'1': page(1, [offer(100, 10)]), '2': page(2, []), '9': page(9, [offer(13270, 0, products=[])], 'builders_club_frontpage')}}
    normal['index']['children'][0]['localization'] = 'Anna'
    bc_tree = node(-1, 'root', [node(1, 'set_anna', [node(5, 'bc_only_sub')]), node(7, 'bc_blocks'), node(8, 'bc_frontpage')])
    bc_tree['children'][0]['children'].append(dict(node(6, ''), localization='Anna_sub'))
    bc = {'index': bc_tree, 'pages': {
        '1': page(1, [offer(200, 10), offer(201, 11, priceInCredits=4), offer(100, 12, priceInCredits=2)]),
        '5': page(5, [offer(300, 13)]), '6': page(6, [offer(301, 14)]), '7': page(7, [offer(302, 15)]),
        '8': page(8, [offer(13270, 0, products=[], localizationId='builders_club_14_days')], 'builders_club_frontpage')}}
    return dict(normal, buildersClub=bc)


def test_builders_club_pages_merge_by_link_or_caption_path_and_offers_by_furni():
    merged, report = m.merge_builders_club(builders_club_case())
    pages = merged['pages']
    anna = [(o['offerId'], o['products'][0]['furniClassId'], o['priceInCredits']) for o in pages['1']['offers']]
    # furni 10 stays on its NORMAL offer; 11 is BC-only at its BC price; BC offer 100 collides with a NORMAL id and moves.
    assert anna == [(100, 10, 3), (201, 11, 4), (100 + m.BUILDERS_CLUB_OFFER_OFFSET, 12, 2)]
    tree = {n['pageId']: n for n in m.iter_nodes(merged['index'])}
    assert [c['pageId'] for c in tree[1]['children']] == [2, 5]          # caption twin 'Anna_sub' merged into page 2
    assert [o['offerId'] for o in pages['2']['offers']] == [301]
    assert 7 in tree and pages['7']['offers'][0]['offerId'] == 302       # BC-only page kept
    assert 9 not in tree and 8 not in tree and '9' not in pages          # Builders Club's own pages left out
    assert 'buildersClub' not in merged and report['captured'] and report['bc_offer_ids_moved'] == 1


def test_a_furni_normal_sells_elsewhere_keeps_its_normal_offer():
    case = builders_club_case()
    case['buildersClub']['pages']['7'] = page(7, [offer(999, 10, priceInCredits=1)])
    merged, report = m.merge_builders_club(case)
    assert [(o['offerId'], o['priceInCredits']) for o in merged['pages']['7']['offers']] == [(100, 3)]
    assert report['normal_offers_added'] == 1


def test_without_a_builders_club_capture_nothing_changes():
    catalog = {'index': node(-1, 'root', [node(1, 'a')]), 'pages': {'1': page(1, [])}}
    assert m.merge_builders_club(catalog) == (catalog, {'captured': False})


def test_builders_club_sentinel_prices_take_the_furni_line_price():
    case = builders_club_case()
    case['pages']['1']['offers'].append(offer(110, 20, priceInCredits=6))          # NORMAL sells line 'hygge' at 6
    case['buildersClub']['pages']['7'] = page(7, [offer(400, 21, priceInCredits=1, priceInActivityPoints=10000, activityPointType=105),
                                                   offer(401, 30, priceInCredits=10000, priceInActivityPoints=1, activityPointType=4),
                                                   offer(402, 31, priceInCredits=1), offer(403, 40, priceInCredits=10000)])
    lines = {20: 'hygge', 21: 'hygge', 30: 'blocks', 31: 'blocks'}
    merged, report = m.merge_builders_club(case, lambda kind, class_id: lines.get(class_id))
    prices = {o['offerId']: (o['priceInCredits'], o['priceInActivityPoints'], o['activityPointType']) for o in merged['pages']['7']['offers']}
    assert prices == {400: (6, 0, 0), 401: (1, 0, 0), 402: (1, 0, 0), 403: (3, 0, 0)}
    assert report['bc_sentinel_prices_replaced'] == 3


# ---- Staff tab ---------------------------------------------------------------

def test_staff_tree_pages_by_category_with_big_categories_split_by_line(monkeypatch):
    monkeypatch.setattr(m, 'STAFF_FOLDER_SIZE', 6)
    monkeypatch.setattr(m, 'STAFF_SMALL_LINE', 3)
    unsold = [dict(entry(f'chair{n}', id=10 + n, category='chair', furniline='plasto'), kind='s', target=100 + n) for n in range(2)]
    unsold += [dict(entry(f'big{n}', id=20 + n, category='other', furniline='rare'), kind='s', target=200 + n) for n in range(4)]
    unsold += [dict(entry(f'small{n}', id=30 + n, category='other', furniline=line), kind='i', target=300 + n)
               for n, line in enumerate(['alpha', 'beta', 'gamma', 'zulu'])]
    custom = [{'id': 5, 'type': 's', 'item_name': 'a0 pet5', 'sprite_id': 9}, {'id': 1000000226, 'type': 'i', 'item_name': 'camera', 'sprite_id': 8}]
    tables, report = m.staff_tree(unsold, custom, 7, lambda link: False)
    pages = {row['id']: row for row in tables['catalog_pages'].values()}
    assert [p['path'] for p in report] == ['Staff', 'Staff > Chair', 'Staff > Other', 'Staff > Other > rare', 'Staff > Other > Other lines A–Z',
                                           'Staff > Plus custom']
    assert all(p['required_permission'] == m.STAFF_PERMISSION for p in pages.values())
    other = next(p for p in pages.values() if p['caption'] == 'Other')
    assert other['enabled'] == 0 and not any(key[0] == other['id'] for key in tables['catalog_page_images'])
    assert all(p['enabled'] == 1 for p in pages.values() if p['caption'] != 'Other')
    root = pages[m.STAFF_PAGE_ID_BASE]
    assert (root['parent_id'], root['link'], root['position']) == (None, 'staff', 7)
    assert [o['id'] for o in tables['catalog_offers'].values()][:2] == [m.STAFF_FLOOR_OFFER_BASE + 10, m.STAFF_FLOOR_OFFER_BASE + 11]
    assert (m.STAFF_WALL_OFFER_BASE + 30,) in tables['catalog_offers']
    assert {(m.STAFF_CUSTOM_OFFER_BASE + 1,), (m.STAFF_CUSTOM_OFFER_BASE + 2,)} <= set(tables['catalog_offers'])
    assert all((o['cost_credits'], o['cost_points'], o['club_level']) == (0, 0, 0) for o in tables['catalog_offers'].values())
    assert tables['catalog_offer_products'][(m.STAFF_CUSTOM_OFFER_BASE + 1, 0)]['furniture_id'] == 5


def test_staff_link_is_left_out_when_the_public_tree_uses_it():
    tables, _ = m.staff_tree([], [], 0, lambda link: link == 'staff')
    assert tables['catalog_pages'][(m.STAFF_PAGE_ID_BASE,)]['link'] is None


# ---- references and precedence -------------------------------------------------

def refs_evidence(habs=None, **sources):
    return m.Evidence(habs or {}, {name: votes for name, votes in sources.items()}, {'wf_trg_says_something': 'wired_trigger'})


def vote(interaction, modes=1, vending='0'):
    return {'interaction': interaction, 'modes': modes, 'vending_ids': vending, 'multiheight': ''}


def test_reference_names_translate_to_names_plusemu_parses():
    valid = m.interaction_names() | m.wired_box_names().keys()
    for name, plus in m.REFERENCE_INTERACTIONS.items():
        assert plus in valid, (name, plus)
    assert m.translate_reference('vendingmachine_no_sides', valid) == 'vendingmachine'
    assert m.translate_reference('pet12', valid) == 'pet'
    assert m.translate_reference('gate', valid) == 'gate'
    assert m.translate_reference('wf_blob', valid) is None


def test_two_references_agreeing_decide_and_a_lone_one_needs_the_bundle():
    gate = {('s', 'xmas_gate'): vote('gate', 2)}
    two = refs_evidence(a=gate, b=gate)
    assert m.reference_consensus('s', 'xmas_gate', None, two)['rule'] == 'references:a+b'
    lone = refs_evidence(a=gate)
    assert m.reference_consensus('s', 'xmas_gate', None, lone)['interaction'] is None
    multistate = {'logicType': 'furniture_multistate', 'visualizationType': 'furniture_animated', 'height': 1, 'states': 2}
    assert m.reference_consensus('s', 'xmas_gate', multistate, lone)['rule'] == 'references:a+hab'
    clash = refs_evidence(a=gate, b={('s', 'xmas_gate'): vote('teleport')})
    result = m.reference_consensus('s', 'xmas_gate', multistate, clash)
    assert result['interaction'] is None and result['conflict'] == ['a=gate', 'b=teleport']
    unmapped = refs_evidence(a={('s', 'blob'): vote('wf_blob', 3)})
    assert m.reference_consensus('s', 'blob', None, unmapped)['unmapped'] == [('a', 'wf_blob')]


def test_reference_modes_need_agreement():
    assert m.reference_consensus('s', 'x', None, refs_evidence(a={('s', 'x'): vote('default', 4)}))['modes'] == 4
    split = refs_evidence(a={('s', 'x'): vote('default', 4)}, b={('s', 'x'): vote('default', 2)})
    assert m.reference_consensus('s', 'x', None, split)['modes'] is None


def test_items_base_rows_read_column_lists_and_multi_row_inserts(tmp_path):
    sql = tmp_path / 'base.sql'
    sql.write_text("INSERT INTO `items_base` (`id`, `sprite_id`, `public_name`, `item_name`, `type`, `interaction_type`, `interaction_modes_count`) VALUES\n"
                   "(1, 10, 'It''s a gate', 'xmas_gate', 's', 'gate', 2),\n(2, 11, 'Semi; colon', 'lamp', 's', 'default', 3);\n"
                   "INSERT INTO `items_base` VALUES (127, 127, 'bar_polyfon', 'Mini-bar', 's', 1, 1, 1.00, 1, 0, 0, 0, 1, 1, 0, 0, 1, "
                   "'vendingmachine', 0, '6,5,2,1', '', '', 0, 0, '', '17', '0');\n", encoding='latin-1')
    votes = m.reference_votes(sql)
    assert votes[('s', 'xmas_gate')]['interaction'] == 'gate' and votes[('s', 'xmas_gate')]['modes'] == 2
    assert votes[('s', 'lamp')]['modes'] == 3
    assert votes[('s', 'bar_polyfon')] == {'interaction': 'vendingmachine', 'modes': 0, 'vending_ids': '6,5,2,1', 'multiheight': ''}


def test_precedence_plus_configuration_then_wired_then_habbo_then_references():
    habbo = {('s', 'gate_x'): entry('gate_x', id=20), ('s', 'wf_trg_says_something'): entry('wf_trg_says_something', id=30, category='wired'),
             ('s', 'lamp'): entry('lamp', id=40, category='lighting')}
    furniture = [row(1, 'gate_x', 20), row(2, 'gate_x ', 21, owner=0, interaction='gate', vending_ids='0'),   # Plus configured a renamed copy
                 row(3, 'wf_trg_says_something', 30, interaction='default'), row(4, 'wf_trg_says_something', 30, owner=0, interaction='wf_trg_attime'),
                 row(5, 'lamp', 40), row(6, 'a0 custom', 99, owner=0)]
    habs = {'lamp': {'logicType': 'furniture_multistate', 'visualizationType': 'furniture_animated', 'height': 1, 'states': 3}}
    lamp_votes = {('s', 'lamp'): vote('dimmer', 2)}
    evidence = refs_evidence(habs, a=lamp_votes, b=lamp_votes, c={('s', 'a0 custom'): vote('default', 5)})
    result = m.plan_furniture(furniture, habbo, set(), evidence)
    updates, report = result['updates'], result['report']
    rules = {d['id']: (d['interaction'], d['rule']) for d in report['derived']}
    assert rules[1] == ('gate', 'plus:original')
    assert rules[3] == ('wf_trg_says_something', 'wired:registry')
    assert rules[4] == ('wf_trg_says_something', 'wired:override') and report['wired_overrides'][0]['from'] == 'wf_trg_attime'
    assert rules[5] == ('dimmer', 'references:a+b')
    assert updates[5]['interaction_modes_count'] == 3                          # the .hab wins over the references' 2
    assert report['modes']['hab_vs_references'] == [{'classname': 'lamp', 'hab': 3, 'references': 2}]
    assert updates[6]['interaction_modes_count'] == 5                          # no .hab: the references
    assert 'interaction_modes_count' not in updates.get(3, {})                 # wired boxes keep theirs


@pytest.mark.parametrize('classname,interaction', [
    ('wf_storage_furni1', 'wired_chest_furni'),
    ('wf_storage_furni2', 'wired_chest_furni'),
    ('wf_storage_furni_starter', 'wired_chest_furni'),
    ('wf_storage_coins1', 'wired_chest_coins'),
    ('wf_storage_coins2', 'wired_chest_coins'),
    ('wf_contract_payment', 'wired_contract_payment'),
    ('wf_contract_reward', 'wired_contract_reward'),
    ('wf_contract_trade', 'wired_contract_trade'),
])
def test_chest_and_contract_imports_store_concrete_interactions_without_runtime_aliases(classname, interaction):
    wired = m.wired_box_names()
    assert m.derive_interaction('s', entry(classname), None, None, wired) == (interaction, 'classname')
    evidence = m.Evidence(wired=wired)
    habbo = {('s', classname): entry(classname, id=123)}
    furniture = [row(7, classname, 123, interaction='default')]
    repair = m.plan_furniture(furniture, habbo, set(), evidence, {})
    assert repair['updates'][7]['interaction_type'] == interaction
    apply_furniture(furniture, repair)
    assert not m.plan_furniture(furniture, habbo, set(), evidence, {})['updates']
    assert m.plan_furniture([], habbo, {('s', classname)}, evidence, {})['inserts'][0]['interaction_type'] == interaction


def test_area_hide_import_uses_canonical_interaction_without_runtime_asset_alias():
    assert m.derive_interaction('s', entry('conf_area_hide'), None, None, m.wired_box_names()) == ('area_hide', 'classname')
