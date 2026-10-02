#!/usr/bin/env python3
"""Retrieve only manifest-missing original Wired SWFs and public source metadata."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
import html
import json
from pathlib import Path
import re
import struct
from urllib.parse import urlencode
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET
import zlib

ROOT = Path(__file__).resolve().parents[1]
BASE = 'https://habbofurni.com'
# Original source classnames explicitly mapped by the audited registry's reference implementation.
ORIGINAL_NAMES = {
    'wf_xtra_var_fx_boss': 'wf_xtra_varfx_boss',
    'wf_xtra_var_fx_health': 'wf_xtra_varfx_hp',
    'wf_xtra_var_fx_level': 'wf_xtra_varfx_levelling',
    'wf_xtra_var_fx_number': 'wf_xtra_varfx_number',
    'wf_xtra_var_fx_progress': 'wf_xtra_varfx_prog',
    'wf_xtra_var_fx_status': 'wf_xtra_varfx_status',
}


def get(url):
    with urlopen(Request(url, headers={'User-Agent': 'Mozilla/5.0', 'Accept': '*/*'}), timeout=45) as response:
        return response.read()


def swf_model(data, classname):
    if data[:3] not in (b'FWS', b'CWS'):
        raise ValueError('Not a native SWF: ' + classname)
    body = zlib.decompress(data[8:]) if data[:3] == b'CWS' else data[8:]
    if len(body) + 8 != struct.unpack_from('<I', data, 4)[0]:
        raise ValueError('Invalid SWF length: ' + classname)
    bits = body[0] >> 3
    position = (5 + 4 * bits + 7) // 8 + 4
    symbols, binaries = {}, {}
    while position + 2 <= len(body):
        tag = struct.unpack_from('<H', body, position)[0]; position += 2
        kind, size = tag >> 6, tag & 63
        if size == 63:
            size = struct.unpack_from('<I', body, position)[0]; position += 4
        content = body[position:position + size]; position += size
        if kind == 76:
            count = struct.unpack_from('<H', content)[0]; offset = 2
            for _ in range(count):
                identity = struct.unpack_from('<H', content, offset)[0]; offset += 2
                end = content.index(b'\0', offset)
                symbols[identity] = content[offset:end].decode(); offset = end + 1
        elif kind == 87:
            binaries[struct.unpack_from('<H', content)[0]] = content[6:]
    xml = {symbols[key]: value for key, value in binaries.items() if key in symbols}
    logic = ET.fromstring(xml[classname + '_' + classname + '_logic'])
    index = ET.fromstring(xml[classname + '_index'])
    if logic.attrib.get('type') != classname or index.attrib.get('type') != classname:
        raise ValueError('SWF classname mismatch: ' + classname)
    dimensions = logic.find('./model/dimensions').attrib
    directions = [int(node.attrib['id']) for node in logic.findall('./model/directions/direction')]
    return {'dimensions': {key: float(dimensions[key]) for key in ('x', 'y', 'z')},
            'directions': directions, 'logic_type': index.attrib['logic'],
            'visualization_type': index.attrib['visualization']}


def fetch(entry, output):
    canonical = entry['name']
    classname = ORIGINAL_NAMES.get(canonical, canonical)
    inventory_url = BASE + '/furniture?' + urlencode({'categoryFilter': 'wired', 'search': classname})
    inventory = get(inventory_url).decode()
    matches = []
    for identity, card in re.findall(r'<li wire:key="(\d+)"(.*?)</li>', inventory, re.S):
        image = re.search(r'classname=([^&]+)&revision=(\d+)', card)
        if image and image.group(1) == classname:
            matches.append((int(identity), int(image.group(2))))
    if not matches:
        inventory_url = BASE + '/furniture?' + urlencode({'search': classname})
        inventory = get(inventory_url).decode()
        for identity, card in re.findall(r'<li wire:key="(\d+)"(.*?)</li>', inventory, re.S):
            image = re.search(r'classname=([^&]+)&revision=(\d+)', card)
            if image and image.group(1) == classname:
                matches.append((int(identity), int(image.group(2))))
    if len(matches) != 1:
        raise ValueError('Public inventory is not unambiguous for ' + classname)
    identity, revision = matches[0]
    view_url = BASE + '/furniture/' + str(identity)
    page = get(view_url).decode()
    exports = []
    for pre in re.findall(r'<pre[^>]*>(.*?)</pre>', page, re.S):
        text = html.unescape(re.sub(r'<[^>]+>', '', pre))
        if text.strip().startswith('{'):
            exports.append(json.loads(text))
    if len(exports) != 1:
        raise ValueError('Missing public metadata export: ' + classname)
    metadata = exports[0]['roomitemtypes']['furnitype'][0]
    if metadata['classname'] != classname or metadata['revision'] != revision:
        raise ValueError('Public metadata revision/name mismatch: ' + classname)
    # The site's export id is its own record ID, NOT the hotel SpriteId.
    ids = set()
    for li in re.findall(r'<li class="px-4 py-3 sm:px-6 flex items-center justify-between">(.*?)</li>', page, re.S):
        text = ' '.join(html.unescape(re.sub(r'<[^>]+>', ' ', li)).split())
        if re.fullmatch(r'Furni id \d+', text):
            ids.add(int(text.split()[-1]))
    if len(ids) != 1:
        raise ValueError('Hotel SpriteId history is absent/ambiguous: ' + classname)
    sprite_id = ids.pop()
    swf_url = BASE + '/furni_assets/' + str(revision) + '/' + classname + '.swf'
    data = get(swf_url)
    model = swf_model(data, classname)
    if (metadata['xdim'], metadata['ydim']) != (model['dimensions']['x'], model['dimensions']['y']):
        raise ValueError('Public/SWF dimensions mismatch: ' + classname)
    export_id = metadata['id']
    metadata['id'] = sprite_id
    metadata_path = output / 'metadata' / (classname + '.json')
    metadata_bytes = (json.dumps(metadata, indent=2) + '\n').encode()
    metadata_path.write_bytes(metadata_bytes)
    swf_path = output / 'swf' / (classname + '.swf')
    swf_path.write_bytes(data)
    icon_url = BASE + '/furni_assets/' + str(revision) + '/' + classname + '_icon.png'
    if icon_url not in page:
        raise ValueError('Original icon URL is absent from public metadata: ' + classname)
    icon = get(icon_url)
    if not icon.startswith(b'\x89PNG\r\n\x1a\n'):
        raise ValueError('Original icon is not PNG: ' + classname)
    icon_path = output / 'icons' / (classname + '_icon.png')
    icon_path.write_bytes(icon)
    return {'canonical_name': canonical, 'asset_classname': classname, 'sprite_id': sprite_id,
            'revision': revision, 'inventory_url': inventory_url, 'view_url': view_url,
            'site_record_id': identity, 'site_export_id_rejected': export_id,
            'sprite_id_evidence': 'unambiguous public revision-history Furni id; export record id rejected',
            'swf_url': swf_url, 'swf_path': str(swf_path.relative_to(output)),
            'swf_sha256': hashlib.sha256(data).hexdigest(), 'swf_bytes': len(data),
            'metadata_path': str(metadata_path.relative_to(output)),
            'metadata_sha256': hashlib.sha256(metadata_bytes).hexdigest(),
            'public_page_sha256': hashlib.sha256(page.encode()).hexdigest(),
            'icon_url': icon_url, 'icon_path': str(icon_path.relative_to(output)),
            'icon_sha256': hashlib.sha256(icon).hexdigest(), 'icon_bytes': len(icon),
            'model': model, 'conversion_status': 'native_swf_pending_nitro'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True, help='New isolated artifact directory; never the shared asset directory.')
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        raise ValueError('Output already exists; preserve prior source evidence.')
    output.mkdir(parents=True)
    (output / 'swf').mkdir(); (output / 'metadata').mkdir(); (output / 'icons').mkdir()
    manifest = json.loads((ROOT / 'Database/WiredCatalog/manifest.json').read_text())
    missing = [entry for entry in manifest['entries'] if entry['asset_status'] != 'verified' or entry.get('asset_source') == 'original_overlay']
    results, errors = [], []
    with ThreadPoolExecutor(max_workers=3) as executor:
        jobs = [(entry['name'], executor.submit(fetch, entry, output)) for entry in missing]
        for name, job in jobs:
            try:
                row = job.result(); results.append(row)
                print(json.dumps({'name': name, 'sprite_id': row['sprite_id'], 'revision': row['revision'], 'status': row['conversion_status']}), flush=True)
            except Exception as error:
                errors.append({'canonical_name': name, 'error': str(error)})
                print(json.dumps(errors[-1]), flush=True)
    receipt = {'retrieved_at': datetime.now(timezone.utc).isoformat(), 'source': BASE + '/furniture?categoryFilter=wired',
               'registry_commit': manifest['registry_commit'], 'entries': results, 'blocked': errors}
    (output / 'downloads.json').write_text(json.dumps(receipt, indent=2) + '\n')
    if errors:
        raise SystemExit('Some originals remain blocked; see downloads.json.')


if __name__ == '__main__':
    main()
