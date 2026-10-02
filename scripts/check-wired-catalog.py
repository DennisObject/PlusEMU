#!/usr/bin/env python3
"""Exercise the importer against a disposable MariaDB copy of catalogue-only data."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import time

SPEC = importlib.util.spec_from_file_location('wired_catalog', Path(__file__).with_name('import-wired-catalog.py'))
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)
TEST_CONTAINER = 'plus-wired-catalog-check'


class CopiedDatabase(module.Database):
    def __init__(self):
        # Test-only password; no ports, network or existing volumes.
        self.process = subprocess.Popen(['docker', 'exec', '-i', TEST_CONTAINER, 'mariadb',
            '-uroot', '-pcatalog-test-only', '--batch', '--raw', '--skip-column-names', '--unbuffered', 'plus'],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)


def fail_plan(manifest, ledger, snapshot, expected):
    try:
        module.plan(manifest, ledger, snapshot)
    except ValueError as error:
        assert expected in str(error), str(error)
    else:
        raise AssertionError('Conflicting input accepted: ' + expected)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--asset-overlay', type=Path)
    parser.add_argument('--support-ledger', type=Path, required=True)
    args = parser.parse_args()
    manifest = json.loads(module.MANIFEST.read_text())
    ledger = json.loads(args.support_ledger.read_text())
    module.validate_assets(manifest, args.assets, args.asset_overlay)
    module.validate_ledger(manifest, ledger)
    source = module.Database()
    try:
        source.query('START TRANSACTION READ ONLY;')
        snapshot = module.read_snapshot(source)
        source.query('ROLLBACK;')
    finally:
        source.close()
    module.guard()
    schema = subprocess.check_output(['docker', 'exec', module.CONTAINER, 'sh', '-c',
        'MYSQL_PWD="$MARIADB_ROOT_PASSWORD" exec mariadb-dump -uroot --no-data --skip-comments plus furniture catalog_pages catalog_items'], text=True)
    # Only start when the exact test name is unclaimed. Never remove somebody else's container.
    if subprocess.run(['docker', 'container', 'inspect', TEST_CONTAINER], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0:
        raise ValueError('Disposable test container already exists; refusing to replace it.')
    image = subprocess.check_output(['docker', 'inspect', '--format', '{{.Image}}', module.CONTAINER], text=True).strip()
    subprocess.run(['docker', 'run', '--detach', '--name', TEST_CONTAINER, '--network', 'none',
        '--tmpfs', '/var/lib/mysql:rw', '--env', 'MARIADB_ROOT_PASSWORD=catalog-test-only',
        '--env', 'MARIADB_DATABASE=plus', image], check=True, stdout=subprocess.DEVNULL)
    db = None
    try:
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            ready = subprocess.run(['docker', 'exec', TEST_CONTAINER, 'mariadb', '-uroot',
                                    '-pcatalog-test-only', '-e', 'SELECT 1'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if ready.returncode == 0:
                break
            time.sleep(0.5)
        else:
            raise ValueError('Disposable MariaDB did not become ready.')
        # Dump contains schema only. Seed rows contain furniture/catalogue data, no identities/items.
        seed = schema + '\n' + '\n'.join(module.insert(table, row) for table, rows in snapshot.items() for row in rows)
        subprocess.run(['docker', 'exec', '-i', TEST_CONTAINER, 'mariadb', '-uroot', '-pcatalog-test-only', 'plus'],
                       input=seed, text=True, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        db = CopiedDatabase()
        before = module.read_snapshot(db)
        assert before == snapshot, 'Schema copy did not preserve source rows.'
        result = module.plan(manifest, ledger, before)
        db.query('SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; START TRANSACTION;')
        module.read_snapshot(db, True)
        db.query(module.statements(result))
        after = module.read_snapshot(db, True)
        rerun = module.plan(manifest, ledger, after)
        assert not rerun['definitions'] and not rerun['offers'] and not rerun['new_page'], 'Import is not idempotent.'
        for table, rows in before.items():
            actual = {r['id']: r for r in after[table]}
            assert all(actual[r['id']] == r for r in rows), 'Existing row changed: ' + table
        assert all(r['wired_id'] == 0 for r in after['furniture'] if r['id'] not in {x['id'] for x in before['furniture']}), 'New definition claimed a legacy ID.'
        db.query('ROLLBACK;')
        assert module.read_snapshot(db) == before, 'Rollback changed existing tables.'
        # Session loss with pending inserts must also roll back.
        db.query('START TRANSACTION;')
        db.query(module.statements(result))
        db.close(); db = CopiedDatabase()
        assert module.read_snapshot(db) == before, 'Disconnect failed to roll back.'
        # Commit then rerun: allocated IDs are actual database IDs, not sprite IDs.
        db.query('START TRANSACTION;')
        db.query(module.statements(result))
        db.query('COMMIT;')
        committed = module.read_snapshot(db)
        rerun = module.plan(manifest, ledger, committed)
        assert not rerun['offers'] and not rerun['definitions'], 'Committed rerun has mutations.'
        assert module.statements(rerun) == '', 'No-op rerun generated SQL.'
        disabled = dict(ledger, boxes=[dict(row, support='DescriptorOnly') for row in ledger['boxes']],
                        auxiliaries=[dict(row, supported=False) for row in ledger.get('auxiliaries', [])])
        unpublished = module.plan(manifest, disabled, before)
        assert not unpublished['definitions'] and not unpublished['offers'], 'Unsupported boxes were published.'
        supported = result['reused'] or result['definitions']
        if supported:
            name = supported[0]['name']
            conflict = json.loads(json.dumps(committed))
            row = next(r for r in conflict['furniture'] if r['item_name'] == name)
            row['sprite_id'] = -1
            fail_plan(manifest, ledger, conflict, 'conflicting sprite')
            conflict = json.loads(json.dumps(committed))
            row = next(r for r in conflict['furniture'] if r['item_name'] == name)
            conflict['furniture'].append(dict(row, id=2000000000))
            fail_plan(manifest, ledger, conflict, 'Ambiguous existing')
        owned_page = next((p for p in committed['catalog_pages'] if p['page_link'] == module.PAGE_LINK), None)
        if owned_page:
            assert owned_page['caption'] == 'Recently Added', 'New page has the wrong user caption.'
            assert owned_page['page_strings_1'] == 'catalog_wired_header1|', 'New page does not reference the genuine wired header asset.'
            published = {r['catalog_name']: r for r in committed['catalog_items'] if r['page_id'] == owned_page['id']}
            assert all(e['name'] in published for e in result['definitions']), 'New definition is absent from Recently Added.'
            for entry in result['definitions']:
                row = next(r for r in committed['furniture'] if r['item_name'] == entry['name'])
                assert row['sprite_id'] == entry['sprite_id'] and row['interaction_type'] == entry['interaction'], 'Original canonical server identity changed.'
            conflict = json.loads(json.dumps(committed))
            conflict['catalog_pages'].append(dict(owned_page, id=2000000000))
            fail_plan(manifest, ledger, conflict, 'Duplicate import page')
            conflict = json.loads(json.dumps(committed))
            next(p for p in conflict['catalog_pages'] if p['id'] == owned_page['id'])['caption'] = 'Other owner'
            fail_plan(manifest, ledger, conflict, 'different page configuration')
        # ID exhaustion produces a SQL failure; all pending rows must disappear.
        db.query('ALTER TABLE furniture AUTO_INCREMENT=4294967295;')
        db.query('START TRANSACTION;')
        missing = [e for e in manifest['entries'] if e['asset_status'] == 'verified'][:2]
        exhausted = {'new_page': False, 'page_id': owned_page['id'] if owned_page else 0, 'definitions': missing,
                     'offers': [{'name': e['name'], 'definition_id': None} for e in missing]}
        try:
            db.query(module.statements(exhausted))
        except ValueError:
            pass
        else:
            raise AssertionError('ID exhaustion did not fail.')
        db.close(); db = CopiedDatabase()
        assert module.read_snapshot(db) == committed, 'ID exhaustion left partial data.'
        print(json.dumps({'engine_commit': ledger['engineCommit'], 'factory_implemented': result['factory_implemented'], 'auxiliary_supported': result['auxiliary_supported'], 'copied_rows': {k: len(v) for k, v in snapshot.items()},
                         'new_definitions': len(result['definitions']), 'new_offers': len(result['offers']),
                         'reused_definitions': len(result['reused']), 'excluded': len(result['excluded']),
                         'checks': ['asset hashes/geometry/png', 'copied real MariaDB schema', 'existing rows and legacy IDs preserved',
                                    'rollback', 'disconnect rollback', 'committed idempotence', 'sprite/name conflicts' if supported else 'no supported identities to collide',
                                    'page conflicts' if owned_page else 'no import page to collide', 'ID exhaustion rollback', 'unsupported excluded', 'Recently Added/canonical identity' if owned_page else 'no unsupported page created', 'original assets only']}))
    finally:
        if db:
            db.close()
        subprocess.run(['docker', 'rm', '--force', TEST_CONTAINER], check=True, stdout=subprocess.DEVNULL)


if __name__ == '__main__':
    main()
