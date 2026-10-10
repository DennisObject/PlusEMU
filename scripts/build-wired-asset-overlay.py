#!/usr/bin/env python3
"""Audit converted originals and build a new private overlay; never replace base assets."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import struct
import subprocess
import zlib

ROOT = Path(__file__).resolve().parents[1]
CONVERTER_COMMIT = 'e0a1800a83feda5f9b1b5cfde7fed0181de7b06f'
CONVERTER_LOCK = 'ba5a8fd9da1dd844f5850c9f5dd6478dd4c245c8067cb4c8bc3cf5faa8adadb7'
SPEC = importlib.util.spec_from_file_location('wired_sources', Path(__file__).with_name('fetch-wired-original-assets.py'))
sources = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sources)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def bundle_model(data, classname):
    position, files = 2, {}
    for _ in range(struct.unpack_from('>H', data)[0]):
        length = struct.unpack_from('>H', data, position)[0]; position += 2
        name = data[position:position + length].decode(); position += length
        length = struct.unpack_from('>I', data, position)[0]; position += 4
        if name in files or position + length > len(data):
            raise ValueError('Invalid Nitro file entry: ' + classname)
        files[name] = zlib.decompress(data[position:position + length]); position += length
    if position != len(data) or set(files) != {classname + '.json', classname + '.png'}:
        raise ValueError('Unexpected Nitro bundle files: ' + classname)
    asset = json.loads(files[classname + '.json'])
    png = files[classname + '.png']
    if asset['name'] != classname or not png.startswith(b'\x89PNG\r\n\x1a\n'):
        raise ValueError('Native classname or PNG disagrees: ' + classname)
    width, height = struct.unpack_from('>II', png, 16)
    sheet = asset['spritesheet']
    if sheet['meta']['image'] != classname + '.png' or sheet['meta']['size'] != {'w': width, 'h': height}:
        raise ValueError('Spritesheet texture size/name disagrees: ' + classname)
    if not sheet['frames']:
        raise ValueError('No renderer frames: ' + classname)
    for frame in sheet['frames'].values():
        box = frame['frame']
        if min(box.values()) < 0 or box['x'] + box['w'] > width or box['y'] + box['h'] > height:
            raise ValueError('Sprite frame exceeds native texture: ' + classname)
    return {'dimensions': asset['logic']['model']['dimensions'],
            'directions': asset['logic']['model']['directions'],
            'logic_type': asset['logicType'], 'visualization_type': asset['visualizationType']}, len(sheet['frames'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--downloads', type=Path, required=True, help='Private artifact directory containing downloads.json, metadata/ and swf/.')
    parser.add_argument('--converted', type=Path, required=True, help='Pinned converter output directory with exactly the downloaded .nitro files.')
    parser.add_argument('--converter', type=Path, required=True)
    parser.add_argument('--assets', type=Path, required=True, help='Read-only base furniture directory.')
    parser.add_argument('--output', type=Path, required=True, help='New private furniture directory; must not overlap base assets.')
    args = parser.parse_args()
    output, base = args.output.resolve(), args.assets.resolve()
    if output.exists() or output == base or base in output.parents or output in base.parents:
        raise ValueError('Output must be new and separate from the base asset tree.')
    if subprocess.check_output(['git', '-C', str(args.converter), 'rev-parse', 'HEAD'], text=True).strip() != CONVERTER_COMMIT:
        raise ValueError('Converter revision differs from the reviewed pin.')
    if subprocess.check_output(['git', '-C', str(args.converter), 'diff', 'HEAD', '--', 'src', 'package.json', 'yarn.lock'], text=True):
        raise ValueError('Converter source or dependencies changed.')
    if digest((args.converter / 'yarn.lock').read_bytes()) != CONVERTER_LOCK:
        raise ValueError('Converter dependency lock changed.')
    manifest = json.loads((ROOT / 'Database/WiredCatalog/manifest.json').read_text())
    receipts = json.loads((args.downloads / 'downloads.json').read_text())
    expected = {e['name'] for e in manifest['entries'] if e['asset_status'] != 'verified' or e.get('asset_source') == 'original_overlay'}
    if receipts['blocked'] or receipts['registry_commit'] != manifest['registry_commit'] or len(receipts['entries']) != len(expected) or {r['canonical_name'] for r in receipts['entries']} != expected:
        raise ValueError('Original receipts must cover the reviewed missing names exactly once.')
    base_bytes = (base / 'json/FurnitureData.json').read_bytes()
    if digest(base_bytes) != manifest['furnidata_sha256']:
        raise ValueError('Base FurniData changed.')
    merged = json.loads(base_bytes)
    entries = merged['roomitemtypes']['furnitype']
    old_count = len(entries)
    classnames, ids = {e['classname'] for e in entries}, {e['id'] for e in entries}
    names = {r['asset_classname'] + '.nitro' for r in receipts['entries']}
    if {p.name for p in args.converted.glob('*.nitro')} != names:
        raise ValueError('Conversion must contain exactly the targeted original bundles.')
    additions, native = [], []
    for receipt in receipts['entries']:
        name, classname = receipt['canonical_name'], receipt['asset_classname']
        if classname != sources.ORIGINAL_NAMES.get(name, name):
            raise ValueError('Unreviewed asset classname alias: ' + name)
        swf = (args.downloads / receipt['swf_path']).read_bytes()
        metadata_bytes = (args.downloads / receipt['metadata_path']).read_bytes()
        if digest(swf) != receipt['swf_sha256'] or digest(metadata_bytes) != receipt['metadata_sha256']:
            raise ValueError('Downloaded source changed: ' + name)
        icon = (args.downloads / receipt['icon_path']).read_bytes()
        if digest(icon) != receipt['icon_sha256'] or not icon.startswith(b'\x89PNG\r\n\x1a\n'):
            raise ValueError('Downloaded original icon changed: ' + name)
        metadata = json.loads(metadata_bytes)
        if (metadata['classname'], metadata['id'], metadata['revision']) != (classname, receipt['sprite_id'], receipt['revision']):
            raise ValueError('Source metadata identity disagrees: ' + name)
        if metadata['id'] == receipt['site_export_id_rejected'] or metadata['id'] in ids or classname in classnames:
            raise ValueError('Original SpriteId/classname collision or website record ID: ' + name)
        data = (args.converted / (classname + '.nitro')).read_bytes()
        model, frames = bundle_model(data, classname)
        if model != receipt['model'] or model != sources.swf_model(swf, classname):
            raise ValueError('Converted/native SWF model disagrees: ' + name)
        if (metadata['xdim'], metadata['ydim']) != (model['dimensions']['x'], model['dimensions']['y']):
            raise ValueError('Original geometry disagrees: ' + name)
        # HabboFurni serializes an absent XML color list as null; Volt iterates an array.
        client_metadata = json.loads(json.dumps(metadata))
        normalized = []
        if client_metadata.get('partcolors', {}).get('color') is None:
            client_metadata['partcolors'] = {'color': []}
            normalized.append('partcolors.color:null/absent -> [] (empty original color list)')
        ids.add(metadata['id']); classnames.add(classname); additions.append(client_metadata)
        native.append({**receipt, 'conversion_status': 'native_nitro_verified',
                       'nitro_path': 'nitro/' + classname + '.nitro', 'nitro_sha256': digest(data),
                       'nitro_bytes': len(data), 'sprite_frames': frames,
                       'client_metadata_normalizations': normalized})
    entries.extend(additions)
    output.mkdir(parents=True); (output / 'nitro').mkdir(); (output / 'json').mkdir(); (output / 'icons').mkdir()
    for receipt in native:
        shutil.copyfile(args.converted / (receipt['asset_classname'] + '.nitro'), output / receipt['nitro_path'])
        shutil.copyfile(args.downloads / receipt['icon_path'], output / 'icons' / (receipt['asset_classname'] + '_icon.png'))
    merged_bytes = (json.dumps(merged, separators=(',', ':'), ensure_ascii=False) + '\n').encode()
    (output / 'json/FurnitureData.json').write_bytes(merged_bytes)
    provenance = {**receipts, 'entries': native, 'base_furnidata_sha256': digest(base_bytes),
                  'overlay_furnidata_sha256': digest(merged_bytes), 'preserved_floor_entries': old_count,
                  'added_floor_entries': len(additions), 'all_other_furnidata_sections_preserved': True,
                  'converter': {'repository': 'https://github.com/billsonnn/nitro-converter',
                                'commit': CONVERTER_COMMIT, 'yarn_lock_sha256': CONVERTER_LOCK},
                  'alias_evidence': {'repository': 'Polaris-Emulator', 'commit': '34bc0d49511c659fd0054d35d957669ebd6ce9ed',
                                     'path': 'Emulator/src/main/java/com/eu/habbo/habbohotel/items/ItemManager.java',
                                     'meaning': 'Six var_fx canonical and varfx original names register the same concrete variable-effect interaction.'}}
    (output / 'original-assets.json').write_text(json.dumps(provenance, indent=2) + '\n')
    print(json.dumps({'preserved_floor_entries': old_count, 'added_originals': len(native),
                      'overlay_furnidata_sha256': digest(merged_bytes), 'output': str(output)}))


if __name__ == '__main__':
    main()
