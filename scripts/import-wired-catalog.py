#!/usr/bin/env python3
"""Plan an additive catalogue import; writes require explicit isolated-preview opt-in."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
import zlib

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / 'Database/WiredCatalog/manifest.json'
CONTAINER = 'plus-wired-preview-db-1'
PAGE_LINK = 'plus_recently_added_wired_v1'
TABLES = {
    'furniture': ['id', 'item_name', 'sprite_id', 'type', 'width', 'length', 'stack_height',
                  'interaction_type', 'wired_id', 'public_name', 'can_stack', 'can_sit', 'is_walkable',
                  'allow_recycle', 'allow_trade', 'allow_marketplace_sell', 'allow_gift',
                  'allow_inventory_stack', 'behaviour_data', 'interaction_modes_count', 'vending_ids',
                  'height_adjustable', 'effect_id', 'is_rare', 'clothing_id', 'extra_rot'],
    'catalog_pages': ['id', 'parent_id', 'page_link', 'caption', 'page_layout', 'min_rank',
                      'visible', 'enabled', 'icon_image', 'min_vip', 'order_num', 'page_strings_1', 'page_strings_2'],
    'catalog_items': ['id', 'page_id', 'item_id', 'catalog_name', 'cost_credits', 'cost_pixels',
                      'cost_diamonds', 'amount', 'limited_sells', 'limited_stack', 'offer_active',
                      'extradata', 'badge', 'offer_id'],
}


def guard():
    # Never dump Config.Env: the secret remains inside the selected database container.
    fmt = '{{json .Config.Labels}}\n{{json .Mounts}}\n{{json .NetworkSettings.Networks}}'
    output = subprocess.check_output(['docker', 'inspect', '--format', fmt, CONTAINER], text=True)
    labels, mounts, networks = map(json.loads, output.splitlines())
    if labels.get('com.docker.compose.project') != 'plus-wired-preview' or labels.get('com.docker.compose.service') != 'db':
        raise ValueError('Database is outside the exact isolated project/service.')
    data = [m for m in mounts if m['Destination'] == '/var/lib/mysql']
    if len(data) != 1 or data[0].get('Name') != 'plus-wired-preview_wired-db-data':
        raise ValueError('Database does not use the exact isolated volume.')
    if set(networks) != {'plus-wired-preview_default'}:
        raise ValueError('Database is connected outside the isolated network.')


class Database:
    def __init__(self):
        guard()
        self.process = subprocess.Popen(
            ['docker', 'exec', '-i', CONTAINER, 'sh', '-c',
             'MYSQL_PWD="$MARIADB_ROOT_PASSWORD" exec mariadb -uroot --batch --raw --skip-column-names --unbuffered plus'],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def query(self, sql):
        self.process.stdin.write(sql + "\nSELECT '{\"end\":true}';\n")
        self.process.stdin.flush()
        rows = []
        while True:
            line = self.process.stdout.readline()
            if not line:
                raise ValueError('Isolated database command failed; transaction is rolled back on disconnect.')
            row = json.loads(line)
            if row == {'end': True}:
                return rows
            rows.append(row)

    def close(self):
        self.process.stdin.close()
        self.process.wait(timeout=15)
        self.process.stdout.close()
        self.process.stderr.close()


def read_snapshot(db, lock=False):
    engines = db.query("SELECT JSON_OBJECT('table',TABLE_NAME,'engine',ENGINE) FROM information_schema.TABLES "
                       "WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('furniture','catalog_pages','catalog_items');")
    if len(engines) != len(TABLES) or any(row['engine'] != 'InnoDB' for row in engines):
        raise ValueError('All three catalogue tables must exist and use transactional InnoDB.')
    snapshot = {}
    for table, columns in TABLES.items():
        pairs = ','.join(f"'{col}'," + (f"CAST(`{col}` AS UNSIGNED)" if col in ('visible', 'enabled') else f"`{col}`") for col in columns)
        # Full index scans lock records and gaps on apply. No hotel users/items are read.
        snapshot[table] = db.query(f'SELECT JSON_OBJECT({pairs}) FROM `{table}` ORDER BY id' + (' FOR UPDATE;' if lock else ';'))
    return snapshot


def validate_assets(manifest, assets, overlay=None):
    furnidata = assets / 'json/FurnitureData.json'
    if hashlib.sha256(furnidata.read_bytes()).hexdigest() != manifest['furnidata_sha256']:
        raise ValueError('FurniData changed; review and regenerate the manifest before importing.')
    entries = json.loads(furnidata.read_text())['roomitemtypes']['furnitype']
    indexed = {e['classname']: e for e in entries}
    original_rows = [row for row in manifest['entries'] if row.get('asset_source') == 'original_overlay']
    overlay_indexed = {}
    if original_rows:
        if overlay is None:
            raise ValueError('Reviewed original asset overlay required: supply --asset-overlay.')
        overlay_file = overlay / 'json/FurnitureData.json'
        if hashlib.sha256(overlay_file.read_bytes()).hexdigest() != manifest['overlay_furnidata_sha256']:
            raise ValueError('Original overlay FurniData changed; review before importing.')
        merged = json.loads(overlay_file.read_text())
        baseline = json.loads(furnidata.read_text())
        additions = [r for r in merged['roomitemtypes']['furnitype'] if r['classname'] not in indexed]
        if {r['classname'] for r in additions} != {r['asset_classname'] for r in original_rows}:
            raise ValueError('Overlay must add exactly the reviewed original classnames.')
        preserved = json.loads(json.dumps(merged))
        preserved['roomitemtypes']['furnitype'] = [r for r in merged['roomitemtypes']['furnitype'] if r['classname'] in indexed]
        if preserved != baseline:
            raise ValueError('Overlay changed existing FurniData entries or metadata.')
        if len({r['id'] for r in additions}) != len(additions) or any(r['id'] in {e['id'] for e in entries} for r in additions):
            raise ValueError('Original overlay SpriteId collision.')
        overlay_indexed = {e['classname']: e for e in additions}
    for row in manifest['entries']:
        if row['asset_status'] != 'verified':
            continue
        name = row['name']
        classname = row.get('asset_classname', name)
        source = overlay if row.get('asset_source') == 'original_overlay' else assets
        data = (source / 'nitro' / (classname + '.nitro')).read_bytes()
        if hashlib.sha256(data).hexdigest() != row['asset_sha256']:
            raise ValueError('Asset changed: ' + name)
        if row.get('asset_source') == 'original_overlay' and hashlib.sha256((source / 'icons' / (classname + '_icon.png')).read_bytes()).hexdigest() != row['icon_sha256']:
            raise ValueError('Original icon changed: ' + name)
        f = (overlay_indexed if row.get('asset_source') == 'original_overlay' else indexed)[classname]
        if (f['id'], f['xdim'], f['ydim'], f['defaultdir']) != (row['sprite_id'], row['width'], row['length'], row['default_direction']):
            raise ValueError('FurniData geometry disagrees: ' + name)
        position = 2
        model = None
        png = False
        for _ in range(struct.unpack_from('>H', data)[0]):
            size = struct.unpack_from('>H', data, position)[0]; position += 2
            filename = data[position:position + size].decode(); position += size
            size = struct.unpack_from('>I', data, position)[0]; position += 4
            content = zlib.decompress(data[position:position + size]); position += size
            if filename.endswith('.json'):
                asset = json.loads(content)
                if asset['name'] != classname:
                    raise ValueError('Asset classname disagrees: ' + name)
                model = asset['logic']['model']
            elif filename.endswith('.png'):
                png = content.startswith(b'\x89PNG\r\n\x1a\n')
        if not png or not model or model['dimensions'] != {'x': row['width'], 'y': row['length'], 'z': row['height']} or model['directions'] != row['directions']:
            raise ValueError('Asset geometry/texture disagrees: ' + name)


def validate_ledger(manifest, ledger):
    if not re.fullmatch('[0-9a-f]{40}', ledger['engineCommit']):
        raise ValueError('Support ledger needs an immutable engine commit.')
    if ledger['registryCommit'] != manifest['registry_commit']:
        raise ValueError('Support ledger is for a different registry.')
    canonical = {e['name'] for e in manifest['entries'] if e['category'] != 'Auxiliary'}
    rows = ledger['boxes']
    if len(rows) != len(canonical) or {r['name'] for r in rows} != canonical:
        raise ValueError('Factory support inventory must cover all canonical boxes exactly once.')
    if any(r['support'] not in ('Implemented', 'DescriptorOnly') for r in rows):
        raise ValueError('Unknown factory support status.')
    support = {r['name']: r['support'] == 'Implemented' for r in rows}
    auxiliary = {e['name']: e['interaction'] for e in manifest['entries'] if e['category'] == 'Auxiliary'}
    seen = set()
    for row in ledger.get('auxiliaries', []):
        if row['name'] not in auxiliary or row['name'] in seen or row['interaction'] != auxiliary[row['name']] or not isinstance(row['supported'], bool):
            raise ValueError('Invalid auxiliary support/interaction evidence.')
        seen.add(row['name'])
        support[row['name']] = row['supported']
    return support


def plan(manifest, ledger, snapshot):
    support = validate_ledger(manifest, ledger)
    pages = [p for p in snapshot['catalog_pages'] if p['page_link'].lower() == PAGE_LINK]
    if len(pages) > 1:
        raise ValueError('Duplicate import page link.')
    page = pages[0] if pages else None
    expected_page = dict(parent_id=-1, caption='Recently Added', page_layout='default_3x3', min_rank=1,
                         visible=1, enabled=1, icon_image=1, min_vip=0, order_num=999,
                         page_strings_1='catalog_wired_header1|',
                         page_strings_2='Wired furniture available in this engine.|')
    if page and any(page[key] != value for key, value in expected_page.items()):
        raise ValueError('Import page link belongs to a different page configuration.')
    definitions, offers, excluded, reused = [], [], [], []
    for entry in manifest['entries']:
        name = entry['name']
        if entry['asset_status'] != 'verified' or not support.get(name, False):
            excluded.append({'name': name, 'reason': entry['asset_status'] if entry['asset_status'] != 'verified' else 'factory_not_implemented'})
            continue
        matches = [r for r in snapshot['furniture'] if r['item_name'].lower() == name]
        if len(matches) > 1:
            raise ValueError('Ambiguous existing definition: ' + name)
        row = matches[0] if matches else None
        if row:
            # Existing geometry and legacy wired_id remain untouched; runtime resolves by name.
            if row['type'] != 's' or row['sprite_id'] != entry['sprite_id']:
                raise ValueError('Existing definition has conflicting sprite/type: ' + name)
            reused.append({'name': name, 'id': row['id'], 'wired_id': row['wired_id'],
                           'interaction': row['interaction_type'], 'geometry_matches':
                           (row['width'], row['length'], row['stack_height']) == (entry['width'], entry['length'], entry['height'])})
        else:
            collisions = [r for r in snapshot['furniture'] if r['sprite_id'] == entry['sprite_id']]
            if collisions:
                excluded.append({'name': name, 'reason': 'sprite_already_owned', 'existing_names': [r['item_name'] for r in collisions]})
                continue
            definitions.append(entry)
        current = [r for r in snapshot['catalog_items'] if page and r['page_id'] == page['id'] and r['catalog_name'].lower() == name]
        if len(current) > 1:
            raise ValueError('Duplicate import offer: ' + name)
        if current:
            expected = dict(item_id=str(row['id']) if row else None, cost_credits=0, cost_pixels=0,
                            cost_diamonds=0, amount=1, limited_sells=0, limited_stack=0,
                            offer_active='1', extradata='', badge='', offer_id=-1)
            if any(current[0][k] != value for k, value in expected.items()):
                raise ValueError('Import offer has conflicting item/pricing: ' + name)
        else:
            offers.append({'name': name, 'definition_id': row['id'] if row else None})
    return {'engine_commit': ledger['engineCommit'],
            'factory_implemented': sum(r['support'] == 'Implemented' for r in ledger['boxes']),
            'auxiliary_supported': sum(r['supported'] for r in ledger.get('auxiliaries', [])),
            'new_page': bool(offers and not page),
            'page_id': page['id'] if page else None, 'definitions': definitions, 'offers': offers,
            'reused': reused, 'excluded': excluded}


def literal(value):
    if isinstance(value, str):
        return "CONVERT(0x" + value.encode().hex() + " USING utf8mb4)" if value else "''"
    return str(int(value)) if isinstance(value, bool) else str(value)


def insert(table, row):
    return f'INSERT INTO `{table}` (' + ','.join('`' + k + '`' for k in row) + ') VALUES (' + ','.join(literal(v) for v in row.values()) + ');'


def statements(result):
    sql = []
    if result['new_page']:
        sql += [insert('catalog_pages', dict(parent_id=-1, caption='Recently Added', icon_image=1,
                 visible=1, enabled=1, min_rank=1, min_vip=0, order_num=999, page_link=PAGE_LINK,
                 page_layout='default_3x3', page_strings_1='catalog_wired_header1|',
                 page_strings_2='Wired furniture available in this engine.|')), 'SET @wired_page=LAST_INSERT_ID();']
    elif result['offers']:
        sql.append(f"SET @wired_page={result['page_id']};")
    new = {r['name']: r for r in result['definitions']}
    for offer in result['offers']:
        name = offer['name']
        if name in new:
            row = new[name]
            sql.append(insert('furniture', dict(item_name=name, public_name=row['public_name'], type='s',
                width=row['width'], length=row['length'], stack_height=row['height'], can_stack='1',
                can_sit=str(int(row['can_sit'])), is_walkable=str(int(row['walkable'])), sprite_id=row['sprite_id'],
                interaction_type=row['interaction'], interaction_modes_count=1, wired_id=0,
                extra_rot=str(int(row['extra_rot'])), allow_trade='1',
                allow_marketplace_sell='1', allow_gift='1', allow_inventory_stack='1')))
            sql.append('SET @wired_item=LAST_INSERT_ID();')
        else:
            sql.append(f"SET @wired_item={offer['definition_id']};")
        # Variables are created only by this function, never from input SQL.
        sql.append('INSERT INTO catalog_items (page_id,item_id,catalog_name,cost_credits,cost_pixels,cost_diamonds,amount,offer_id) '
                   + 'VALUES (@wired_page,CAST(@wired_item AS CHAR),' + literal(name) + ',0,0,0,1,-1);')
    return '\n'.join(sql)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets', type=Path, required=True, help='Trusted furniture directory with json/ and nitro/.')
    parser.add_argument('--asset-overlay', type=Path, help='Reviewed private furniture directory containing the 22 original bundles and merged FurniData.')
    parser.add_argument('--support-ledger', type=Path, required=True, help='Reviewed, actual factory probe inventory.')
    parser.add_argument('--snapshot', type=Path, help='Offline definition/catalogue inventory; never written to the database.')
    parser.add_argument('--report', type=Path, help='Write full reviewable plan JSON.')
    parser.add_argument('--apply-isolated', action='store_true', help='Opt in AFTER root review; exact project/volume guard.')
    parser.add_argument('--expected-engine-commit', help='Full reviewed engine hash required on apply.')
    args = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text())
    ledger = json.loads(args.support_ledger.read_text())
    validate_assets(manifest, args.assets, args.asset_overlay)
    if args.apply_isolated and (args.snapshot or args.expected_engine_commit != ledger['engineCommit']):
        raise ValueError('Apply requires online guarded DB and the exact reviewed engine hash.')
    db = None
    try:
        if args.snapshot:
            snapshot = json.loads(args.snapshot.read_text())
        else:
            db = Database()
            if args.apply_isolated:
                lock = db.query("SELECT JSON_OBJECT('acquired',GET_LOCK('plus_wired_catalog_v1',10));")
                if lock != [{'acquired': 1}]:
                    raise ValueError('Import lock unavailable.')
                db.query('SET SESSION innodb_lock_wait_timeout=10; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; START TRANSACTION;')
            else:
                db.query('START TRANSACTION READ ONLY;')
            snapshot = read_snapshot(db, args.apply_isolated)
        result = plan(manifest, ledger, snapshot)
        if args.report:
            args.report.write_text(json.dumps(result, indent=2) + '\n')
        if args.apply_isolated:
            db.query(statements(result))
            after = plan(manifest, ledger, read_snapshot(db, True))
            if after['new_page'] or after['definitions'] or after['offers']:
                raise ValueError('Post-import idempotence check failed.')
            db.query('COMMIT;')
        elif db:
            db.query('ROLLBACK;')
        print(json.dumps({'mode': 'applied-isolated' if args.apply_isolated else 'dry-run',
                         'engine_commit': result['engine_commit'], 'factory_implemented': result['factory_implemented'],
                         'auxiliary_supported': result['auxiliary_supported'], 'new_definitions': len(result['definitions']),
                         'new_offers': len(result['offers']), 'reused_definitions': len(result['reused']),
                         'excluded': len(result['excluded']), 'new_page': result['new_page']}))
    finally:
        if db:
            # On any failure EOF closes the SQL session, rolling back and releasing GET_LOCK.
            db.close()


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError, subprocess.SubprocessError) as error:
        sys.exit(str(error))
