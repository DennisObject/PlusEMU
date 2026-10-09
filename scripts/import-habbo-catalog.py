#!/usr/bin/env python3
"""Replace the catalogue with a habbo.com capture and make furniture follow Habbo's furnidata.

  plan    reads the database, prints counts and writes the JSON report; nothing is written.
  apply   writes the planned differences in one transaction, then re-plans inside it and commits only when
          nothing is left to do, so a second apply is a no-op.
  fetch-habs    downloads official .hab bundles (logicType, states) for the interaction evidence.
  fetch-assets  downloads the .hab bundles and icons a plan report lists as missing into a staging folder.

Inputs: the parsed capture (habbo-catalog/capture/parse.py), Habbo's furnidata.json (and optionally
productdata.json and external_flash_texts.txt), a folder of official .hab files, Arcturus' catalog.sql for
interaction votes, and `rclone lsf` listings of the R2 furniture bundles and icons.
"""
import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta
import gzip
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
import unicodedata
from urllib.error import HTTPError
from urllib.request import Request, urlopen
import zlib

ROOT = Path(__file__).resolve().parents[1]
WIRED_REGISTRY = ROOT / 'HabboHotel/Items/Wired/Configuration/WiredBoxRegistry.cs'
INTERACTION_TYPES = ROOT / 'HabboHotel/Items/InteractionTypes.cs'
HOF_FURNI = 'https://images.habbo.com/dcr/hof_furni'
# Index folders (pageId -1 on habbo.com) are stored as disabled pages, which the emulator sends as -1 headings.
FOLDER_PAGE_ID_BASE = 2000000000
# The hidden page holding habbo.com's HC gift offers.
CLUB_GIFT_PAGE_ID = FOLDER_PAGE_ID_BASE
LOCK_NAME = 'plus_habbo_catalog_import'


# ---- HAB bundles ----------------------------------------------------------

def read_hab(data):
    """{file name: bytes} of an official .hab bundle (LE 'HAB\\0', version 1, flags 1, zlib JSON index)."""
    if len(data) < 20 or data[:4] != b'HAB\0':
        raise ValueError('not a HAB bundle')
    version, flags, index_stored, index_length, payload_length = struct.unpack_from('<HHIII', data, 4)
    if (version, flags) != (1, 1) or 20 + index_stored + payload_length != len(data):
        raise ValueError('unsupported or truncated HAB bundle')
    index = json.loads(zlib.decompress(data[20:20 + index_stored]))
    payload = data[20 + index_stored:]
    files = {}
    for entry in index['entries']:
        content = payload[entry['offset']:entry['offset'] + entry['storedLength']]
        files[entry['name']] = zlib.decompress(content) if entry['compression'] == 'deflate' else content
    return files


def hab_logic(data):
    """What a bundle says about behaviour: logicType, visualizationType, model height and state count."""
    document = next(json.loads(content) for name, content in read_hab(data).items() if name.endswith('.json'))
    states = set()
    for visualization in document.get('visualizations') or []:
        # Animation ids below 1000 are states; higher ids are transitions between them.
        states.update(int(key) for key in (visualization.get('animations') or {}) if str(key).isdigit() and int(key) < 1000)
    dimensions = ((document.get('logic') or {}).get('model') or {}).get('dimensions') or {}
    return {'logicType': document.get('logicType'), 'visualizationType': document.get('visualizationType'),
            'height': dimensions.get('z'), 'states': len(states)}


def library(classname):
    return classname.split('*', 1)[0]


def icon_name(classname):
    name, _, colour = classname.partition('*')
    return f'{name}_{colour}_icon.png' if colour else f'{name}_icon.png'


def download(url, target):
    try:
        with urlopen(Request(url, headers={'User-Agent': 'Mozilla/5.0', 'Accept': '*/*'}), timeout=45) as response:
            data = response.read()
    except HTTPError as error:
        return f'HTTP {error.code}'
    except OSError as error:
        return str(error)
    temporary = target.with_suffix(target.suffix + '.part')
    temporary.write_bytes(data)
    temporary.replace(target)
    return None


def fetch_all(jobs, workers):
    """jobs: [(url, target)]; existing targets are skipped. Returns [{url, error}] for failures."""
    pending = [(url, target) for url, target in jobs if not target.exists()]
    failures = []
    with ThreadPoolExecutor(max_workers=workers) as pool:
        for (url, _), error in zip(pending, pool.map(lambda job: download(*job), pending)):
            if error:
                failures.append({'url': url, 'error': error})
    return {'requested': len(jobs), 'downloaded': len(pending) - len(failures), 'failures': failures}


def furnidata_entries(furnidata):
    """Habbo's entries as (kind, entry): s = roomitemtypes, i = wallitemtypes."""
    for kind, section in (('s', 'roomitemtypes'), ('i', 'wallitemtypes')):
        for entry in (furnidata.get(section) or {}).get('furnitype') or []:
            yield kind, entry


def fetch_habs(furnidata, cache, workers):
    cache.mkdir(parents=True, exist_ok=True)
    revisions = {}
    for _, entry in furnidata_entries(furnidata):
        name = library(entry['classname'])
        revisions[name] = max(revisions.get(name, 0), int(entry.get('revision') or 0))
    return fetch_all([(f'{HOF_FURNI}/{revision}/{name}.hab', cache / f'{name}.hab') for name, revision in sorted(revisions.items())], workers)


def fetch_assets(report, out, workers):
    """Downloads what the plan reported missing from R2 into out/, laid out as on R2."""
    jobs = []
    for asset in report['assets']['missing']:
        base = f"{HOF_FURNI}/{asset['revision']}/"
        if asset['hab']:
            jobs.append((base + asset['library'] + '.hab', out / 'assets/furniture' / (asset['library'] + '.hab')))
        if asset['icon']:
            jobs.append((base + asset['icon'], out / 'c_images/hof_furni/icons' / asset['icon']))
    for _, target in jobs:
        target.parent.mkdir(parents=True, exist_ok=True)
    return fetch_all(sorted(set(jobs)), workers)


# ---- other inputs ---------------------------------------------------------

def load_json(path):
    opener = gzip.open if str(path).endswith('.gz') else open
    with opener(path, 'rt', encoding='utf-8') as f:
        return json.load(f)


WIRED_GENERIC = {'Trigger': 'wired_trigger', 'Action': 'wired_effect', 'Condition': 'wired_condition', 'Selector': 'wired_selector',
                 'Addon': 'wired_addon', 'Variable': 'wired_variable'}


def wired_box_names(path=WIRED_REGISTRY):
    """{box name: its generic wired_* interaction} from PlusEMU's WiredBoxRegistry."""
    return {name: WIRED_GENERIC[category] for name, category in re.findall(r'new\("([a-z0-9_]+)", WiredBoxCategory\.(\w+)', path.read_text())}


def interaction_names(path=INTERACTION_TYPES):
    """Every interaction_type string the emulator parses."""
    return set(re.findall(r'case "([a-z0-9_]+)":', path.read_text()))


# Column order of items_base rows written without a column list (Arcturus dumps).
ARCTURUS_COLUMNS = ['id', 'sprite_id', 'item_name', 'public_name', 'type', 'width', 'length', 'stack_height', 'allow_stack', 'allow_sit',
                    'allow_lay', 'allow_walk', 'allow_gift', 'allow_trade', 'allow_recycle', 'allow_marketplace_sell',
                    'allow_inventory_stack', 'interaction_type', 'interaction_modes_count', 'vending_ids', 'multiheight']


def sql_tuples(text):
    """The value tuples of an INSERT's VALUES list, quoted strings unescaped, NULL as None."""
    rows, row, i, n = [], None, 0, len(text)
    while i < n:
        c = text[i]
        if c == '(' and row is None:
            row = []
        elif c == ')' and row is not None:
            rows.append(row)
            row = None
        elif row is not None and c == "'":
            j, out = i + 1, []
            while text[j] != "'" or text[j + 1:j + 2] == "'":
                if text[j] == '\\':
                    out.append(text[j + 1]); j += 2; continue
                if text[j] == "'":
                    out.append("'"); j += 2; continue
                out.append(text[j]); j += 1
            row.append(''.join(out))
            i = j
        elif row is not None and c not in ', \t\r\n':
            j = i
            while text[j] not in ',)':
                j += 1
            token = text[i:j].strip()
            row.append(None if token.upper() == 'NULL' else token)
            i = j - 1
        i += 1
    return rows


def statement_end(text, i):
    """Index of the ';' ending the SQL statement that continues at i (quotes and backslash escapes respected)."""
    quoted = False
    while True:
        c = text[i]
        if quoted:
            if c == '\\':
                i += 1
            elif c == "'":
                if text[i + 1:i + 2] == "'":
                    i += 1
                else:
                    quoted = False
        elif c == "'":
            quoted = True
        elif c == ';':
            return i
        i += 1


def items_base_rows(path):
    """Rows of every `INSERT INTO items_base` in a dump (one or many tuples per statement, with or without columns)."""
    text = Path(path).read_text(encoding='latin-1')
    for match in re.finditer(r"INSERT INTO `items_base`\s*(?:\(([^)]*)\)\s*)?VALUES\s*", text):
        columns = [c.strip(' `') for c in match[1].split(',')] if match[1] else ARCTURUS_COLUMNS
        end = statement_end(text, match.end())
        for values in sql_tuples(text[match.end():end]):
            yield dict(zip(columns, values))


def reference_votes(path):
    """{(kind, item_name): vote} from a dump's items_base; the first row of a name wins."""
    votes = {}
    for row in items_base_rows(path):
        kind, name = str(row.get('type') or '').strip().lower(), row.get('item_name')
        if kind in ('s', 'i') and name:
            try:
                modes = int(float(row.get('interaction_modes_count') or 0))
            except ValueError:
                modes = 0
            votes.setdefault((kind, name), {'interaction': str(row.get('interaction_type') or '').strip().lower(), 'modes': modes,
                                            'vending_ids': str(row.get('vending_ids') or '0').strip(),
                                            'multiheight': str(row.get('multiheight') or '').strip()})
    return votes


def listing(path):
    """Names in an `rclone lsf` listing."""
    return {line.strip() for line in Path(path).read_text().splitlines() if line.strip()} if path else None


# ---- interaction evidence -------------------------------------------------

COLOURS = {'r': 'red', 'b': 'blue', 'g': 'green', 'y': 'yellow'}
# Furnidata specialtype of floor items -> PlusEMU interaction (the AIR client's special furni types).
SPECIAL_TYPES = {8: 'musicdisc', 9: 'gift', 11: 'trophy', 12: 'exchange', 13: 'horse_body_dye', 14: 'horse_hairstyle',
                 15: 'horse_hair_dye', 23: 'purchasable_clothing'}
WALL_SPECIALS = {'wallpaper': 'wallpaper', 'floor': 'floor', 'landscape': 'landscape'}
# .hab logicType -> interaction. A dict picks by 'category:', 'visualization:' or classname; None is the fallback.
LOGIC_INTERACTIONS = {
    'furniture_trophy': 'trophy', 'furniture_credit': 'exchange', 'furniture_present': 'gift',
    'furniture_crackable': 'crackable_egg', 'furniture_purchasable_clothing': 'purchasable_clothing',
    'furniture_one_way_door': 'onewaygate', 'furniture_habbowheel': 'habbowheel', 'furniture_jukebox': 'jukebox',
    'furniture_sound_machine': 'jukebox', 'furniture_song_disk': 'musicdisc', 'furniture_roomdimmer': 'dimmer',
    'furniture_stickie': 'postit', 'furniture_badge_display': 'badge_display', 'furniture_mannequin': 'mannequin',
    'furniture_background_color': 'roombg', 'furniture_bg': 'background', 'furniture_bb': 'background',
    'furniture_lovelock': 'lovelock', 'furniture_hween_lovelock': 'lovelock', 'furniture_group_forum_terminal': 'guild_forum',
    'furniture_random_teleport': 'hopper', 'furniture_youtube': 'television',
    'furniture_pushable': {'bb_puck': 'banzaipuck', None: 'ball'},
    'furniture_dice': {'bottle': 'bottle', 'visualization:furniture_bottle': 'bottle', None: 'dice'},
    'furniture_guild_customized': {'category:gate': 'gld_gate', None: 'gld_item'},
    'furniture_change_state_when_step_on': {'category:tent': 'tent'},
}
# Furnidata category -> interaction, only for multistate bundles or when no bundle is known.
CATEGORY_INTERACTIONS = {'teleport': 'teleport', 'gate': 'gate', 'roller': 'roller', 'vending_machine': 'vendingmachine',
                         'present': 'gift', 'credit': 'exchange', 'trophy': 'trophy', 'dimmer': 'dimmer'}
# Game pieces and tools whose interaction is named by the piece (as PlusEMU's own rows name them).
CLASSNAME_RULES = [
    (r'fball_goal_([rbgy])', lambda m: COLOURS[m[1]] + '_goal'),
    (r'fball_score_([rbgy])', lambda m: COLOURS[m[1]] + '_score'),
    (r'bb_gate_([rbgy])', lambda m: f'bb_{COLOURS[m[1]]}_gate'),
    (r'bb_score_([rbgy])', lambda m: f'bb_{COLOURS[m[1]]}_score'),
    (r'es_gate_([rbgy])', lambda m: f'freeze{COLOURS[m[1]]}gate'),
    (r'es_score_([rbgy])', lambda m: f'freeze{COLOURS[m[1]]}counter'),
    (r'bb_patch1', 'bb_patch'), (r'bb_counter', 'banzaicounter'), (r'bb_rnd_tele', 'bb_teleport'), (r'bb_puck', 'banzaipuck'),
    (r'bb_pyramid', 'bb_pyramid'), (r'es_counter', 'freezetimer'), (r'es_exit', 'freezeexit'), (r'es_tile', 'freezetile'),
    (r'es_box', 'freezetileblock'), (r'es_tagging', 'icetag_pole'), (r'es_skating_ice', 'iceskates'), (r'fball_gate', 'fbgate'),
    (r'fball_counter', 'counter'), (r'hockey_score', 'scoreboard'), (r'tile_stackmagic[0-9x]*', 'stacktool'),
    (r'tile_walkmagic[0-9x]*', 'tile_walkmagic'), (r'wf_floor_switch[12]', lambda m: m[0]),
    (r'wf_(?:game_)?upcounter[12]', lambda m: m[0]), (r'bottle', 'bottle'), (r'val_randomizer', 'loveshuffler'),
    (r'gld_gate', 'gld_gate'),
]
CLASSNAME_RULES = [(re.compile(pattern), result) for pattern, result in CLASSNAME_RULES]
# The logic types that must agree before a single reference counts.
# Reference interaction names (Arcturus MS 3.5.5, the BoBBa Arcturus dump, Polaris) -> PlusEMU names. A name PlusEMU
# parses itself maps to itself; 'default' means no behaviour; anything else is unmapped and the reference abstains.
REFERENCE_INTERACTIONS = {
    '': 'default', 'normal': 'default', 'switch': 'default', 'multiheight': 'default',
    'clothing': 'purchasable_clothing', 'crackable': 'crackable_egg', 'crackables': 'crackable_egg',
    'pressureplate': 'pressure_pad', 'pressureplate_group': 'pressure_pad', 'floor_switch': 'wf_floor_switch1',
    'love_lock': 'lovelock', 'guild_furni': 'gld_item', 'guild_gate': 'gld_gate', 'club_gate': 'vip_gate',
    'vendingmachine_no_sides': 'vendingmachine', 'vending': 'vendingmachine', 'puzzle_box': 'puzzlebox', 'football': 'ball',
    'trax_machine': 'jukebox', 'stack_helper': 'stacktool', 'tile_walk_magic': 'tile_walkmagic', 'pyramid': 'bb_pyramid',
    'external_image': 'camera_picture', 'tile_fxprovider_nfs': 'fx_provider', 'colorwheel': 'habbowheel', 'spinning_bottle': 'bottle',
    'rollerskate_field': 'rollerskate', 'background_toner': 'roombg', 'ads_bg': 'background', 'youtube': 'television', 'yt_tv': 'television',
    'breeding_nest': 'pet_breeding_box', 'club_hopper': 'hopper', 'costume_hoppper': 'hopper', 'vote_counter': 'scoreboard',
    'football_gate': 'fbgate', 'battlebanzai_puck': 'banzaipuck', 'battlebanzai_random_teleport': 'bb_teleport',
    'battlebanzai_tile': 'bb_patch', 'freeze_tile': 'freezetile', 'freeze_block': 'freezetileblock', 'freeze_exit': 'freezeexit',
    **{f'football_counter_{c}': f'{c}_score' for c in COLOURS.values()}, **{f'football_goal_{c}': f'{c}_goal' for c in COLOURS.values()},
    **{f'battlebanzai_counter_{c}': f'bb_{c}_score' for c in COLOURS.values()}, **{f'battlebanzai_gate_{c}': f'bb_{c}_gate' for c in COLOURS.values()},
    **{f'freeze_counter_{c}': f'freeze{c}counter' for c in COLOURS.values()}, **{f'freeze_gate_{c}': f'freeze{c}gate' for c in COLOURS.values()},
}
REFERENCE_PET = re.compile(r'pet[0-9]+')


def translate_reference(name, valid):
    """The PlusEMU interaction a reference name means, 'default', or None when PlusEMU has no equivalent."""
    name = (name or '').strip().lower()
    if name in REFERENCE_INTERACTIONS:
        return REFERENCE_INTERACTIONS[name]
    if REFERENCE_PET.fullmatch(name):
        return 'pet'
    return name if name in valid else None


def reference_consensus(kind, classname, hab, evidence):
    """What the references decide for a furni: an interaction when two or more agree, or one agrees with the .hab
    logic, and none names another; modes when the references that know it agree."""
    votes = [(source, vote, translate_reference(vote['interaction'], evidence.valid))
             for source, refs in sorted(evidence.refs.items()) for vote in [refs.get((kind, classname))] if vote]
    named = [(source, vote, name) for source, vote, name in votes if name not in (None, 'default')]
    result = {'interaction': None, 'rule': None, 'conflict': None, 'modes': None, 'modes_conflict': None, 'vending_ids': None,
              'multiheight': '', 'unmapped': [(source, vote['interaction']) for source, vote, name in votes if name is None]}
    names = {name for _, _, name in named}
    logic = (hab or {}).get('logicType')
    if len(names) > 1:
        result['conflict'] = sorted(f'{source}={vote["interaction"]}' for source, vote, _ in named)
    elif names:
        name = names.pop()
        if len(named) >= 2 or logic in VOTE_LOGIC.get(name, ()):
            result['interaction'] = name
            result['rule'] = 'references:' + ('+'.join(source for source, _, _ in named) + ('' if len(named) >= 2 else '+hab'))
    for _, vote, _ in named or votes:
        result['vending_ids'] = result['vending_ids'] or vending_ids(vote)
        result['multiheight'] = result['multiheight'] or vote.get('multiheight', '')
    modes = Counter(vote['modes'] for _, vote, _ in votes if vote.get('modes', 0) > 0)
    if len(modes) == 1:
        result['modes'] = next(iter(modes))
    elif modes:
        result['modes_conflict'] = dict(modes)
        top = modes.most_common(2)
        if top[0][1] >= 2 and top[0][1] > top[1][1]:
            result['modes'] = top[0][0]
    return result


VOTE_LOGIC = {
    'gate': {'furniture_multistate'}, 'vendingmachine': {'furniture_multistate', 'furniture_basic'},
    'teleport': {'furniture_multistate'}, 'bed': {'furniture_basic', 'furniture_multistate'}, 'dice': {'furniture_dice'},
    'trophy': {'furniture_trophy'}, 'roller': {'furniture_multistate', 'furniture_basic'},
    'tent': {'furniture_change_state_when_step_on'}, 'badge_display': {'furniture_badge_display'},
    'lovelock': {'furniture_lovelock', 'furniture_hween_lovelock'}, 'gift': {'furniture_present'},
    'onewaygate': {'furniture_one_way_door'}, 'postit': {'furniture_stickie'}, 'dimmer': {'furniture_roomdimmer'},
    'jukebox': {'furniture_jukebox', 'furniture_sound_machine'}, 'mannequin': {'furniture_mannequin'},
    'roombg': {'furniture_background_color'}, 'habbowheel': {'furniture_habbowheel'},
    'stacktool': {'furniture_custom_stack_height'}, 'tile_walkmagic': {'furniture_custom_stack_height'},
    'pressure_pad': {'furniture_multistate', 'furniture_change_state_when_step_on'}, 'crackable_egg': {'furniture_crackable'},
    'hopper': {'furniture_random_teleport'}, 'gld_item': {'furniture_guild_customized'}, 'gld_gate': {'furniture_guild_customized'},
    'ball': {'furniture_pushable'}, 'purchasable_clothing': {'furniture_purchasable_clothing'},
    'musicdisc': {'furniture_song_disk'}, 'bottle': {'furniture_dice'},
}
# interaction_modes_count of these is not a state count (a crackable's is the hits it takes).
MODES_MEAN_OTHER = {'crackable_egg'}
# Logic types of furni that only look and stack; anything else left 'default' is reported.
DECORATIVE_LOGIC = {None, 'furniture_basic', 'furniture_multistate', 'furniture_multiheight', 'furniture_window'}
DECORATIVE_CATEGORIES = {None, '', 'other', 'chair', 'table', 'lighting', 'divider', 'rug', 'shelf', 'floor', 'food',
                         'wall_decoration', 'window', 'extras', 'bed'}


def derivable_interactions():
    """Every fixed interaction name the rules can produce (wired boxes and colour templates aside)."""
    names = {*SPECIAL_TYPES.values(), *WALL_SPECIALS.values(), *CATEGORY_INTERACTIONS.values(), *REFERENCE_INTERACTIONS.values(),
             'postit', 'horse_saddle_1', 'horse_saddle_2', 'gld_gate', 'gld_item', 'bed'}
    for choice in LOGIC_INTERACTIONS.values():
        names |= set(choice.values()) if isinstance(choice, dict) else {choice}
    names |= {result for _, result in CLASSNAME_RULES if isinstance(result, str)}
    names |= {f'{c}_goal' for c in COLOURS.values()} | {f'{c}_score' for c in COLOURS.values()} | {f'bb_{c}_gate' for c in COLOURS.values()} \
        | {f'bb_{c}_score' for c in COLOURS.values()} | {f'freeze{c}gate' for c in COLOURS.values()} | {f'freeze{c}counter' for c in COLOURS.values()}
    return names


def pick(choice, kind, entry, hab):
    if not isinstance(choice, dict):
        return choice
    for key, value in choice.items():
        if key is None:
            continue
        if key.startswith('category:') and entry.get('category') == key[9:]:
            return value
        if key.startswith('visualization:') and (hab or {}).get('visualizationType') == key[14:]:
            return value
        if not key.startswith(('category:', 'visualization:')) and re.fullmatch(key, entry['classname']):
            return value
    return choice.get(None)


def derive_interaction(kind, entry, hab, vote, wired):
    """(interaction, rule) on decisive evidence, in the contract's order; (None, None) when nothing decides."""
    classname, category, special = entry['classname'], entry.get('category'), entry.get('specialtype') or 1
    logic = (hab or {}).get('logicType')
    if kind == 'i':
        if classname in WALL_SPECIALS:
            return WALL_SPECIALS[classname], 'furnidata:classname'
        if special == 5:
            return 'postit', 'furnidata:specialtype=5'
    else:
        if special in SPECIAL_TYPES:
            return SPECIAL_TYPES[special], f'furnidata:specialtype={special}'
        if special == 16 and classname[-1:] in '12':
            return 'horse_saddle_' + classname[-1], 'furnidata:specialtype=16'
        if special == 17:
            if logic == 'furniture_group_forum_terminal':
                return 'guild_forum', 'furnidata:specialtype=17,hab:logicType'
            return ('gld_gate' if category == 'gate' or classname == 'gld_gate' else 'gld_item'), 'furnidata:specialtype=17'
        if entry.get('canlayon'):
            return 'bed', 'furnidata:canlayon'
    if logic in LOGIC_INTERACTIONS:
        interaction = pick(LOGIC_INTERACTIONS[logic], kind, entry, hab)
        if interaction:
            return interaction, f'hab:logicType={logic}'
    if category in CATEGORY_INTERACTIONS and (logic in (None, 'furniture_multistate')
                                              or (category == 'roller' and (hab or {}).get('visualizationType') == 'furniture_queue_tile')):
        interaction = CATEGORY_INTERACTIONS[category]
        # A vending machine hands out items no Habbo file names; without them it is not configured.
        if interaction != 'vendingmachine' or vending_ids(vote):
            return interaction, f'category={category}'
    if classname in wired:
        return classname, 'classname:wired'
    for pattern, result in CLASSNAME_RULES:
        match = pattern.fullmatch(classname)
        if match:
            return (result(match) if callable(result) else result), 'classname'
    return None, None


def vending_ids(vote):
    ids = (vote or {}).get('vending_ids', '0').replace(';', ',')
    return ids if re.fullmatch(r'[0-9]+(,[0-9]+)*', ids) and ids != '0' else None


def hint(kind, entry, hab, vote):
    """Why a row left 'default' may need an interaction, or None for plain decorative furni."""
    logic = (hab or {}).get('logicType')
    reasons = []
    if logic not in DECORATIVE_LOGIC:
        reasons.append(f'logicType={logic}')
    if entry.get('category') not in DECORATIVE_CATEGORIES:
        reasons.append(f'category={entry.get("category")}')
    if (entry.get('specialtype') or 1) != 1:
        reasons.append(f'specialtype={entry.get("specialtype")}')
    voted = (vote or {}).get('interaction', 'default')
    if voted not in ('default', 'switch', 'multiheight', ''):
        reasons.append(f'arcturus={voted}')
    return ', '.join(reasons) or None


def physical_columns(kind, entry, hab):
    """Habbo's physical facts for a row that owns or shares a Habbo entry: size, sit, walk, stacking, stack height and
    the .hab state count. Lying follows from the furnidata column can_lay_on and the bed interaction."""
    columns = {}
    if kind == 's':
        columns.update(width=entry.get('xdim') or 1, length=entry.get('ydim') or 1, can_sit=int(bool(entry.get('cansiton'))),
                       is_walkable=int(bool(entry.get('canstandon'))))
        if entry.get('height') is not None:
            columns['stack_height'] = float(entry['height'])
        if entry.get('canputstuffon') is not None:
            columns['can_stack'] = int(bool(entry['canputstuffon']))
    return columns


def behaviour_columns(kind, entry, hab, vote, interaction):
    """Columns for a row in derivation scope: the physical ones plus what its interaction needs."""
    columns = physical_columns(kind, entry, hab) if entry else {}
    heights = (vote or {}).get('multiheight', '')
    if hab and hab['logicType'] == 'furniture_multiheight' and heights and len(heights.split(';')) == hab['states']:
        columns['height_adjustable'] = heights.replace(';', ',')
    if interaction == 'vendingmachine':
        columns['vending_ids'] = vending_ids(vote)
    return columns


# ---- furniture ------------------------------------------------------------

FURNIDATA_COLUMNS = {
    # column: (furnidata key, default, maximum length)
    'revision': ('revision', 0, None), 'category': ('category', None, 64), 'default_dir': ('defaultdir', 0, None),
    'xdim': ('xdim', 1, None), 'ydim': ('ydim', 1, None), 'name': ('name', None, 255), 'description': ('description', None, 1024),
    'ad_url': ('adurl', None, 512), 'excluded_dynamic': ('excludeddynamic', False, None), 'custom_params': ('customparams', None, 1024),
    'special_type': ('specialtype', 1, None), 'can_stand_on': ('canstandon', False, None), 'can_sit_on': ('cansiton', False, None),
    'can_lay_on': ('canlayon', False, None), 'can_put_stuff_on': ('canputstuffon', None, None), 'height': ('height', None, None),
    'furni_line': ('furniline', None, 64), 'environment': ('environment', None, 64), 'rare': ('rare', False, None),
    'tradeable': ('tradeable', None, None), 'recyclable': ('recyclable', None, None),
}
BEHAVIOUR_COLUMNS = ['item_name', 'public_name', 'type', 'width', 'length', 'stack_height', 'can_stack', 'can_sit', 'is_walkable',
                     'sprite_id', 'allow_recycle', 'allow_trade', 'allow_marketplace_sell', 'allow_gift', 'allow_inventory_stack',
                     'interaction_type', 'interaction_modes_count', 'vending_ids', 'height_adjustable', 'is_rare', 'has_furnidata']
FURNITURE_COLUMNS = ['id'] + BEHAVIOUR_COLUMNS + ['part_colors'] + list(FURNIDATA_COLUMNS)
DEFAULT_INTERACTIONS = ('default', '')


def furnidata_columns(entry):
    """The furniture columns of a furnidata entry, as migration 60 stores them."""
    columns = {}
    for column, (key, default, length) in FURNIDATA_COLUMNS.items():
        value = entry.get(key)
        value = default if value is None else value
        if isinstance(value, bool):
            value = int(value)
        if length and isinstance(value, str):
            value = value[:length]
        columns[column] = value
    colours = (entry.get('partcolors') or {}).get('color') if 'partcolors' in entry else None
    columns['part_colors'] = ','.join(colours or []) if 'partcolors' in entry else None
    return columns


def clothing_parts(entry):
    """The figure set ids a clothing furni unlocks ('3593, 3594,' -> '3593,3594'), or None."""
    parts = [part.strip() for part in (entry.get('customparams') or '').split(',') if part.strip()]
    return ','.join(parts) if parts and all(part.isdigit() for part in parts) else None


def link_key(link):
    """How catalog_pages.link (utf8mb4_uca1400_ai_ci) compares: without case or accents."""
    return ''.join(c for c in unicodedata.normalize('NFKD', link) if not unicodedata.combining(c)).casefold()


def latin1(text, length):
    return ''.join(c if ord(c) < 256 else '?' for c in (text or ''))[:length]


class Evidence:
    """.hab logic by library, reference votes by source, the wired box registry ({name: generic wired_* type})."""

    def __init__(self, habs=None, refs=None, wired=None):
        self.habs, self.refs = habs or {}, refs or {}
        self.wired = wired if isinstance(wired, dict) else {name: None for name in wired or ()}
        self.valid = interaction_names() | set(self.wired)

    def hab(self, classname):
        return self.habs.get(library(classname))


def plan_furniture(furniture, habbo, needed, evidence):
    """Desired furniture rows. furniture: current rows; habbo: {(kind, classname): entry}; needed: (kind, classname)s
    the catalogue sells. Returns updates, inserts and the report."""
    rows = {row['id']: row for row in furniture}
    report = {'renamed': [], 'kind_conflicts': [], 'sprite_moves': [], 'sprite_collisions': [], 'derived': [],
              'left_default_with_hint': [], 'behaviour_updated': 0, 'furnidata_updated': 0, 'created': [],
              'stacking_turned_off': 0, 'agreement': {'agree': 0, 'differ': [], 'undecided': 0}, 'carry_conflicts': [],
              'reference_conflicts': [], 'unmapped_reference_names': {}, 'wired_overrides': [],
              'modes': {'hab': 0, 'references': 0, 'kept': {}, 'unknown': 0, 'hab_vs_references': [], 'reference_conflicts': 0}}
    by_ci = {(kind, classname.lower()): classname for kind, classname in habbo}
    desired = {}  # id -> {column: value}
    entry_of = {}  # id -> (kind, classname) of the Habbo entry the row is or shares
    owner_of = {}  # (kind, classname) -> id
    owners = {(row['type'], row['item_name']): row['id'] for row in furniture if row['has_furnidata']}
    # furnidata_classname is unique across both kinds and compares without case.
    owned_names = {(row['item_name'].lower(), row['type']) for row in furniture if row['has_furnidata']}

    def other_kind_owns(kind, classname):
        return (classname.lower(), 'i' if kind == 's' else 's') in owned_names

    def want(row_id, **columns):
        desired.setdefault(row_id, {}).update(columns)

    for (kind, name), row_id in sorted(owners.items(), key=lambda item: item[1]):
        classname = name if (kind, name) in habbo else by_ci.get((kind, name.lower()))
        if classname is None:
            other = 'i' if kind == 's' else 's'
            if (other, name.lower()) in by_ci:
                report['kind_conflicts'].append({'id': row_id, 'classname': name, 'type': kind, 'habbo_type': other})
            continue
        if classname != name:
            report['renamed'].append({'id': row_id, 'from': name, 'to': classname, 'reason': 'case'})
        owner_of[(kind, classname)] = row_id
        entry_of[row_id] = (kind, classname)
        want(row_id, item_name=classname, has_furnidata=1)

    # Rows whose name only differs by surrounding whitespace or line breaks take the entry when no row owns it.
    junk = {}
    for row in sorted(furniture, key=lambda r: r['id']):
        clean = row['item_name'].strip()
        if not row['has_furnidata'] and clean != row['item_name'] and (row['type'], clean) in habbo and (row['type'], clean) not in owner_of \
                and not other_kind_owns(row['type'], clean):
            junk.setdefault((row['type'], clean), []).append(row['id'])
    for key, ids in junk.items():
        owner_of[key] = ids[0]
        for position, row_id in enumerate(ids):
            report['renamed'].append({'id': row_id, 'from': rows[row_id]['item_name'], 'to': key[1], 'reason': 'whitespace'})
            entry_of[row_id] = key
            want(row_id, item_name=key[1], has_furnidata=int(position == 0))

    # Rows sharing an owner's classname use its entry and its sprite.
    sharing = {}
    for row in furniture:
        owner = owners.get((row['type'], row['item_name']))
        if not row['has_furnidata'] and row['id'] not in entry_of and owner is not None:
            sharing[row['id']] = owner
            if owner in entry_of:
                entry_of[row['id']] = entry_of[owner]
                want(row['id'], item_name=entry_of[owner][1])

    created = []
    for kind, classname in sorted(needed):
        if (kind, classname) in habbo and (kind, classname) not in owner_of:
            if other_kind_owns(kind, classname):
                report['kind_conflicts'].append({'id': None, 'classname': classname, 'type': kind, 'habbo_type': kind,
                                                 'note': 'the other kind owns this classname; not created'})
                continue
            created.append((kind, classname))
            owner_of[(kind, classname)] = ('new', kind, classname)

    # Sprite ids: Habbo's for every entry; rows of other furni that hold one of them move above every id in use.
    sprite = {row['id']: row['sprite_id'] for row in furniture}
    taken = {}
    for (kind, classname), owner in owner_of.items():
        taken[(kind, habbo[(kind, classname)]['id'])] = classname
    ceiling = {kind: max([entry['id'] for (k, _), entry in habbo.items() if k == kind]
                         + [row['sprite_id'] for row in furniture if row['type'] == kind] + [0]) for kind in ('s', 'i')}
    for row in sorted(furniture, key=lambda r: r['id']):
        if row['has_furnidata'] and row['id'] not in entry_of and (row['type'], row['sprite_id']) in taken:
            ceiling[row['type']] += 1
            sprite[row['id']] = ceiling[row['type']]
            report['sprite_collisions'].append({'id': row['id'], 'classname': row['item_name'], 'type': row['type'], 'from': row['sprite_id'],
                                                'to': sprite[row['id']], 'habbo_classname': taken[(row['type'], row['sprite_id'])]})
    for row_id, (kind, classname) in entry_of.items():
        sprite[row_id] = habbo[(kind, classname)]['id']
    for row_id, owner in sharing.items():
        if owner not in entry_of:
            sprite[row_id] = sprite[owner]
    for row in furniture:
        if sprite[row['id']] != row['sprite_id']:
            want(row['id'], sprite_id=sprite[row['id']])
            report['sprite_moves'].append({'id': row['id'], 'classname': row['item_name'], 'type': row['type'], 'from': row['sprite_id'],
                                           'to': sprite[row['id']]})
    for row in furniture:
        if row['id'] not in entry_of and row['id'] not in sharing and not row['has_furnidata'] and (row['type'], row['sprite_id']) in taken \
                and taken[(row['type'], row['sprite_id'])] != row['item_name']:
            report['sprite_collisions'].append({'id': row['id'], 'classname': row['item_name'], 'type': row['type'], 'from': row['sprite_id'],
                                                'to': row['sprite_id'], 'habbo_classname': taken[(row['type'], row['sprite_id'])],
                                                'note': 'row without a furnidata entry; its sprite id names this Habbo entry'})

    # 1. Interactions Plus itself configured, by classname, carry over to its renamed or duplicate rows.
    configured = {}
    for row in furniture:
        if row['interaction_type'].lower() not in DEFAULT_INTERACTIONS and row['interaction_type'].lower() in evidence.valid:
            configured.setdefault(row['item_name'].strip().lower(), {})[row['interaction_type'].lower()] = row
    carry = {}
    for name, options in configured.items():
        if len(options) == 1:
            carry[name] = next(iter(options.values()))
        else:
            report['carry_conflicts'].append({'classname': name, 'interactions': sorted(options)})

    def decide(row_id, kind, classname, entry, current, physical):
        """Columns for one row: its interaction by precedence (Plus' own, wired, Habbo, references) and its modes."""
        hab = evidence.hab(classname) if entry else None
        refs = reference_consensus(kind, classname, hab, evidence)
        if refs['conflict']:
            report['reference_conflicts'].append({'id': row_id, 'classname': classname, 'votes': refs['conflict']})
        for source, name in refs['unmapped']:
            report['unmapped_reference_names'][name] = report['unmapped_reference_names'].get(name, 0) + 1
        vote = {'interaction': '', 'modes': refs['modes'] or 0, 'vending_ids': refs['vending_ids'] or '0', 'multiheight': refs['multiheight']}
        existing = (current or {}).get('interaction_type', 'default')
        default = existing.lower() in DEFAULT_INTERACTIONS
        interaction, rule, extra = None, None, {}
        if not default:
            box = evidence.wired.get(classname, False)
            if box is not False and existing.lower() not in (classname, box):
                interaction, rule = classname, 'wired:override'
                report['wired_overrides'].append({'id': row_id, 'classname': classname, 'from': existing, 'to': classname})
        else:
            source = carry.get(classname.strip().lower())
            if source is not None:
                interaction, rule = source['interaction_type'].lower(), 'plus:original'
                extra = {column: source[column] for column in ('vending_ids', 'height_adjustable', 'effect_id')
                         if source.get(column) not in (None, '0', '', 0)}
            elif classname in evidence.wired:
                interaction, rule = classname, 'wired:registry'
            elif entry is not None:
                interaction, rule = derive_interaction(kind, entry, hab, vote, evidence.wired)
                rule = rule and 'habbo:' + rule
            if interaction is None and refs['interaction'] and (refs['interaction'] != 'vendingmachine' or refs['vending_ids']):
                interaction, rule = refs['interaction'], refs['rule']
        columns = {}
        if entry is not None and default:
            columns.update(behaviour_columns(kind, entry, hab, vote, interaction))
        elif entry is not None and physical:
            columns.update(physical_columns(kind, entry, hab))
        # The emulator unlocks the figure sets in custom_params, so clothing without any stays decorative.
        if interaction == 'purchasable_clothing' and rule != 'plus:original' and not clothing_parts(entry or {}):
            interaction, rule = None, None
        if interaction == 'vendingmachine' and rule == 'plus:original':
            columns.pop('vending_ids', None)
        columns.update(extra)
        if interaction:
            if interaction not in evidence.valid:
                raise ValueError(f'Interaction {interaction!r} for {classname} is not one PlusEMU parses')
            columns['interaction_type'] = interaction
            report['derived'].append({'id': row_id, 'classname': classname, 'type': kind, 'interaction': interaction, 'rule': rule})
        elif default and entry is not None:
            reason = hint(kind, entry, hab, vote)
            if reason:
                report['left_default_with_hint'].append({'id': row_id, 'classname': classname, 'type': kind, 'hint': reason})
        # 5. Modes: the .hab state count, else the references; wired boxes and crackables count something else.
        final = (interaction or existing).lower()
        if final in evidence.wired or final.startswith('wired') or final in MODES_MEAN_OTHER:
            report['modes']['kept'][final if final in MODES_MEAN_OTHER else 'wired'] = report['modes']['kept'].get(
                final if final in MODES_MEAN_OTHER else 'wired', 0) + 1
        elif hab:
            columns['interaction_modes_count'] = max(hab['states'], 1)
            report['modes']['hab'] += 1
            if refs['modes'] and refs['modes'] != columns['interaction_modes_count']:
                report['modes']['hab_vs_references'].append({'classname': classname, 'hab': columns['interaction_modes_count'], 'references': refs['modes']})
        elif refs['modes']:
            columns['interaction_modes_count'] = refs['modes']
            report['modes']['references'] += 1
        else:
            report['modes']['unknown'] += 1
        if refs['modes_conflict']:
            report['modes']['reference_conflicts'] += 1
        return {column: value for column, value in columns.items() if current is None or not same(current.get(column), value)}

    for row_id, (kind, classname) in sorted(entry_of.items()):
        entry, row = habbo[(kind, classname)], rows[row_id]
        owner = bool(row['has_furnidata'] or desired.get(row_id, {}).get('has_furnidata'))
        if owner:
            want(row_id, **furnidata_columns(entry))
        if row['interaction_type'].lower() not in DEFAULT_INTERACTIONS:
            derived, _ = derive_interaction(kind, entry, evidence.hab(classname), None, evidence.wired)
            agreement = report['agreement']
            if derived is None:
                agreement['undecided'] += 1
            elif derived == row['interaction_type'].lower() or evidence.wired.get(derived) == row['interaction_type'].lower():
                agreement['agree'] += 1
            else:
                agreement['differ'].append({'id': row_id, 'classname': classname, 'current': row['interaction_type'], 'derived': derived})
        want(row_id, **decide(row_id, kind, classname, entry, row, owner))
    # Plus rows with no Habbo entry: their own configuration, the wired registry and the references.
    for row in sorted(furniture, key=lambda r: r['id']):
        if row['type'] in ('s', 'i') and row['id'] not in entry_of:
            want(row['id'], **decide(row['id'], row['type'], row['item_name'], None, row, False))

    inserts = []
    for kind, classname in created:
        entry = habbo[(kind, classname)]
        row = {column: None for column in FURNITURE_COLUMNS if column != 'id'}
        row.update(item_name=classname, public_name=latin1(entry.get('name') or classname, 56), type=kind, width=1, length=1,
                   stack_height=0.0, can_stack=1, can_sit=0, is_walkable=0, sprite_id=entry['id'],
                   allow_recycle=int(entry.get('recyclable') is not False), allow_trade=int(entry.get('tradeable') is not False),
                   allow_marketplace_sell=int(entry.get('tradeable') is not False), allow_gift=1, allow_inventory_stack=1,
                   interaction_type='default', interaction_modes_count=1, vending_ids='0', height_adjustable='0',
                   is_rare=int(bool(entry.get('rare'))), has_furnidata=1)
        row.update(furnidata_columns(entry))
        row.update(decide(('new', kind, classname), kind, classname, entry, None, True))
        inserts.append(row)
        report['created'].append({'classname': classname, 'type': kind, 'sprite_id': entry['id'], 'interaction': row['interaction_type']})

    updates = {}
    for row_id, columns in desired.items():
        changed = {column: value for column, value in columns.items() if not same(rows[row_id].get(column), value)}
        if changed.get('can_stack') == 0:
            report['stacking_turned_off'] += 1
        if changed:
            report['furnidata_updated'] += bool(set(changed) & (set(FURNIDATA_COLUMNS) | {'part_colors'}))
            report['behaviour_updated'] += bool(set(changed) & set(BEHAVIOUR_COLUMNS))
            # A row whose sprite or classname changes leaves the unique furnidata keys while the ids move; this restores it.
            if {'sprite_id', 'item_name'} & set(changed):
                changed['has_furnidata'] = columns.get('has_furnidata', rows[row_id]['has_furnidata'])
            updates[row_id] = changed
    return {'updates': updates, 'inserts': inserts, 'owner_of': owner_of, 'report': report}


def same(current, value):
    if isinstance(value, tuple) or isinstance(current, tuple):
        return False
    if current is None or value is None:
        return current is None and value is None
    if isinstance(value, (int, float)) and not isinstance(value, bool) and isinstance(current, (int, float, str)):
        try:
            return abs(float(current) - float(value)) < 1e-9
        except ValueError:
            return False
    return current == value


# ---- catalogue ------------------------------------------------------------

CATALOG_TABLES = {
    'catalog_pages': (('id',), ['id', 'parent_id', 'link', 'caption', 'layout', 'required_permission', 'visible', 'enabled', 'icon',
                                'required_club_level', 'position']),
    'catalog_page_images': (('page_id', 'slot'), ['page_id', 'slot', 'image']),
    'catalog_page_texts': (('page_id', 'slot'), ['page_id', 'slot', 'text']),
    'catalog_offers': (('id',), ['id', 'localization_key', 'cost_credits', 'cost_points', 'points_type', 'club_level', 'bulk_purchase',
                                 'enabled', 'preview_image']),
    'catalog_offer_products': (('offer_id', 'position'), ['offer_id', 'position', 'product_type', 'furniture_id', 'effect_id', 'badge_code',
                                                          'bot_preset_id', 'pet_type', 'habbicon_id', 'amount', 'extra_param']),
    'catalog_page_offers': (('page_id', 'offer_id'), ['page_id', 'offer_id', 'position']),
    'catalog_offer_limited': (('offer_id',), ['offer_id', 'stack', 'sold']),
    'club_gift_offers': (('offer_id',), ['offer_id', 'days_required', 'enabled']),
}
PROMOTION_COLUMNS = ['title', 'image', 'page_link', 'position', 'item_type', 'offer_id', 'product_code', 'expires_at']
PET_OFFER = re.compile(r'a0 pet([0-9]+)')
PRODUCT_TARGETS = ['furniture_id', 'effect_id', 'badge_code', 'bot_preset_id', 'pet_type', 'habbicon_id']


# Builders Club pages and offers that collide with NORMAL ids move up by these offsets.
BUILDERS_CLUB_PAGE_OFFSET = 1000000000
BUILDERS_CLUB_OFFER_OFFSET = 1500000000
BUILDERS_CLUB_ONLY = re.compile(r'(builders_club|loyalty_bc)', re.IGNORECASE)


def merge_builders_club(catalog, line_of=lambda kind, class_id: None):
    """One tree from habbo.com's NORMAL and BUILDERS_CLUB catalogues; PlusEMU has no Builders Club, so all of it sells
    normally. A BC page is the NORMAL page with the same link, or the same caption under the same parent path; matched
    pages get the BC offers for furni they do not list yet. A furni NORMAL sells anywhere keeps its NORMAL offer; a
    BC-only furni keeps its BC offer, whose price habbo.com sends as its catalogue price, except a price of 10,000 or more,
    which marks furni not for sale outside Builders Club: those take their furni line's price. Builders Club's own pages
    and subscription offers are left out. line_of(kind, class id) names a furni's furnidata furniline."""
    bc = catalog.get('buildersClub')
    report = {'captured': bc is not None}
    if bc is None:
        return catalog, report
    pages = {int(page_id): dict(page, offers=list(page['offers'])) for page_id, page in catalog['pages'].items()}
    bc_pages = {int(page_id): page for page_id, page in bc['pages'].items()}
    furni = lambda offer: tuple((p['productType'], p['furniClassId'], p['extraParam'], p['productCount']) for p in offer['products'])
    normal_offer = {}
    for page in pages.values():
        for offer in page['offers']:
            normal_offer.setdefault(furni(offer), offer)
    offer_ids = {offer['offerId'] for page in pages.values() for offer in page['offers']}
    by_link, by_path, path_of = {}, {}, {}
    line_prices = {'normal': {}, 'bc': {}}
    for source, all_pages in (('normal', pages.values()), ('bc', bc_pages.values())):
        for page in all_pages:
            for offer in page['offers']:
                if len(offer['products']) == 1 and not builders_club_sentinel(offer):
                    line = line_of(offer['products'][0]['productType'], offer['products'][0]['furniClassId'])
                    line_prices[source].setdefault(line, Counter())[offer_price(offer)] += 1

    def line_price(offer):
        """The furni line's usual NORMAL price, else its usual Builders Club price, else 3 credits."""
        line = line_of(offer['products'][0]['productType'], offer['products'][0]['furniClassId']) if offer['products'] else None
        for source in ('normal', 'bc'):
            prices = line_prices[source].get(line) if line else None
            if prices:
                return min(prices.items(), key=lambda item: (-item[1], item[0]))[0]
        return (3, 0, 0)

    def remember(node, path):
        if node['pageName']:
            by_link.setdefault(link_key(node['pageName']), node)
        by_path.setdefault(path, node)
        path_of[id(node)] = path

    def index_normal(node, path):
        for child in node['children']:
            child_path = path + (link_key(child['localization']),)
            remember(child, child_path)
            index_normal(child, child_path)

    root = json.loads(json.dumps(catalog['index']))
    index_normal(root, ())
    counts = Counter()

    def merge_offers(target_id, bc_page):
        page = pages[target_id]
        listed = {furni(offer) for offer in page['offers']}
        for offer in bc_page['offers']:
            key = furni(offer)
            if BUILDERS_CLUB_ONLY.match(offer['localizationId']) or not offer['products']:
                counts['bc_subscription_offers'] += 1
            elif key in listed:
                counts['offers_already_listed'] += 1
            elif key in normal_offer:
                page['offers'].append(normal_offer[key])
                counts['normal_offers_added'] += 1
            else:
                moved = offer['offerId'] in offer_ids
                if builders_club_sentinel(offer):
                    credits, points, points_type = line_price(offer)
                    offer = dict(offer, priceInCredits=credits, priceInActivityPoints=points, activityPointType=points_type)
                    counts['bc_sentinel_prices_replaced'] += 1
                page['offers'].append(dict(offer, offerId=offer['offerId'] + BUILDERS_CLUB_OFFER_OFFSET) if moved else offer)
                counts['bc_offers_added'] += 1
                counts['bc_offer_ids_moved'] += moved
            listed.add(key)

    def visit(node, normal_parent):
        for child in node['children']:
            # Captions match under the NORMAL parent the BC parent became.
            child_path = path_of.get(id(normal_parent), ()) + (link_key(child['localization']),)
            twin = (by_link.get(link_key(child['pageName'])) if child['pageName'] else None) or by_path.get(child_path)
            bc_page = bc_pages.get(child['pageId'])
            if bc_page and bc_page['layoutCode'].startswith('builders_club'):
                counts['bc_own_pages_left_out'] += 1
                continue
            if twin is not None and (twin['pageId'] in pages or not (bc_page and bc_page['offers'])):
                counts['pages_merged'] += 1
                if bc_page and twin['pageId'] in pages:
                    merge_offers(twin['pageId'], bc_page)
                visit(child, twin)
                continue
            # A BC page whose twin is a NORMAL folder becomes a page in that folder.
            parent = normal_parent if twin is None else twin
            counts['bc_pages_under_normal_folders'] += twin is not None
            counts['bc_only_pages'] += 1
            page_id = child['pageId']
            if page_id > 0 and page_id in pages:
                page_id += BUILDERS_CLUB_PAGE_OFFSET
                counts['bc_page_ids_moved'] += 1
            copy = dict(child, pageId=page_id, children=[])
            parent['children'].append(copy)
            remember(copy, path_of.get(id(parent), ()) + (link_key(child['localization']),))
            if bc_page:
                pages[page_id] = dict(bc_page, pageId=page_id, catalogType='NORMAL', offers=[])
                merge_offers(page_id, bc_page)
            visit(child, copy)

    visit(bc['index'], root)
    # Builders Club's own pages in the NORMAL tree (subscriptions, add-ons) sell nothing here.
    def prune(node):
        node['children'] = [child for child in node['children']
                            if not (pages.get(child['pageId']) or {}).get('layoutCode', '').startswith('builders_club')]
        for child in node['children']:
            prune(child)
    before = len(pages)
    prune(root)
    kept = {node['pageId'] for node in iter_nodes(root)}
    pages = {page_id: page for page_id, page in pages.items() if page_id in kept}
    counts['bc_own_pages_left_out'] += before - len(pages)
    report.update(counts)
    merged = dict(catalog, index=root, pages={str(page_id): page for page_id, page in pages.items()})
    merged.pop('buildersClub')
    return merged, report


# The Staff tab: every furni no public page sells, for staff only (the permission today's live Staff tab uses).
STAFF_PERMISSION = 'catalog.pages.administrator'
STAFF_PAGE_ID_BASE = 1700000000
# Offer ids: floor 1.8e9 + sprite id, wall 1.85e9 + sprite id; Plus-only rows (ids up to 1e9+, too wide for INT
# offsets) 1.9e9 + their rank by furniture id.
STAFF_FLOOR_OFFER_BASE, STAFF_WALL_OFFER_BASE, STAFF_CUSTOM_OFFER_BASE = 1800000000, 1850000000, 1900000000
STAFF_FOLDER_SIZE, STAFF_SMALL_LINE = 400, 25
STAFF_HEADER, STAFF_TEASER = 'catalog_MOD_Catalog_header', 'catalog_MOD_teaser'
# habbo.com's own captions ("By type") where it has them, readable ones otherwise.
STAFF_CATEGORIES = {
    'bed': ('Bed', 114, 'catalog_beds_header_dyn'), 'chair': ('Chair', 111, 'catalog_chairs_header_dyn'),
    'vending_machine': ('Dispenser', 217, 'catalog_vending_header_dyn'), 'divider': ('Divider', 113, 'catalog_dividers_header_dyn'),
    'floor': ('Floor', 41, 'catalog_floors_header_dyn'), 'lighting': ('Lighting', 115, 'catalog_lighting_header_dyn'),
    'table': ('Table', 112, 'catalog_tables_header_dyn'), 'teleport': ('Teleports', 1, 'catalog_teleports_header_dyn'),
    'wall_decoration': ('Wall decoration', 1, 'catalog_walls_header_dyn'), 'other': ('Other', 1, 'catalog_decorative_header_dyn'),
    'credit': ('Credit furni', 1, None), 'fortuna': ('Fortune & dice', 1, None), 'sound_fx': ('Sound FX', 1, None),
    'wired_add_on': ('Wired add-ons', 1, None), 'wired_condition': ('Wired conditions', 1, None),
    'wired_effect': ('Wired effects', 1, None), 'wired_trigger': ('Wired triggers', 1, None), 'gate': ('Gates', 1, None),
    'trophy': ('Trophies', 1, None), 'rug': ('Rugs', 1, None), 'present': ('Presents', 1, None), 'shelf': ('Shelves', 1, None),
    'roller': ('Rollers', 1, None), 'window': ('Windows', 1, None), 'tent': ('Tents', 1, None), 'dimmer': ('Dimmers', 1, None),
}


def staff_offer_id(kind, sprite_id):
    if not 0 < sprite_id < 50000000:
        raise ValueError(f'Staff offer id: sprite id {sprite_id} out of range')
    return (STAFF_FLOOR_OFFER_BASE if kind == 's' else STAFF_WALL_OFFER_BASE) + sprite_id


def staff_tree(unsold, custom, position, link_taken):
    """Pages, offers, products and placements of the Staff tab. unsold: Habbo furnidata entries (with 'kind' and
    'target' = furniture id or placeholder) no public page sells; custom: Plus rows with no Habbo entry ({id, type,
    item_name, sprite_id}). A category over STAFF_FOLDER_SIZE furni becomes a folder of furniline pages; lines under
    STAFF_SMALL_LINE furni share alphabetical pages. Items sort by furniline, then name."""
    tables = {table: {} for table in CATALOG_TABLES}
    report = []
    next_id = [STAFF_PAGE_ID_BASE]

    def page(parent, caption, icon, header, items, path, link=None, folder=False):
        page_id = next_id[0]
        next_id[0] += 1
        siblings = sum(1 for row in tables['catalog_pages'].values() if row['parent_id'] == parent)
        tables['catalog_pages'][(page_id,)] = dict(
            id=page_id, parent_id=parent, link=link, caption=caption[:128], layout='default_3x3', required_permission=STAFF_PERMISSION,
            # A folder is a disabled heading, sent as page -1 like habbo.com's folders: clicking it only expands it.
            visible=1, enabled=int(not folder), icon=icon, required_club_level=0, position=position if parent is None else siblings)
        if folder:
            report.append({'path': ' > '.join(path), 'id': page_id, 'furni': 0, 'folder': True})
            return page_id
        for slot, image in enumerate((header or STAFF_HEADER, STAFF_TEASER if parent is None else '', '')):
            tables['catalog_page_images'][(page_id, slot)] = dict(page_id=page_id, slot=slot, image=image)
        text = 'Staff only: every furni the public catalogue does not sell.' if parent is None else f'{len(items)} furni'
        for slot, value in enumerate((text, '')):
            tables['catalog_page_texts'][(page_id, slot)] = dict(page_id=page_id, slot=slot, text=value)
        for index, item in enumerate(items):
            offer_id = item['offer_id']
            tables['catalog_offers'][(offer_id,)] = dict(id=offer_id, localization_key=item['classname'][:100], cost_credits=0, cost_points=0,
                                                       points_type=0, club_level=0, bulk_purchase=1, enabled=1, preview_image='')
            tables['catalog_offer_products'][(offer_id, 0)] = dict(offer_id=offer_id, position=0, product_type='furni', furniture_id=item['target'],
                                                                   effect_id=None, badge_code=None, bot_preset_id=None, pet_type=None,
                                                                   habbicon_id=None, amount=1, extra_param='')
            tables['catalog_page_offers'][(page_id, offer_id)] = dict(page_id=page_id, offer_id=offer_id, position=index)
        report.append({'path': ' > '.join(path), 'id': page_id, 'furni': len(items)})
        return page_id

    order = lambda item: ((item['line'] or '').lower(), (item['name'] or '').lower(), item['classname'])
    items = [dict(kind=e['kind'], classname=e['classname'], name=e.get('name'), line=e.get('furniline') or '',
                  category=e.get('category') or 'other', target=e['target'], offer_id=staff_offer_id(e['kind'], e['id']))
             for e in unsold]
    root = page(None, 'Staff', 1, None, [], ['Staff'], link=None if link_taken('staff') else 'staff')
    by_category = {}
    for item in items:
        by_category.setdefault(item['category'], []).append(item)
    caption_of = lambda category: STAFF_CATEGORIES.get(category, (category.replace('_', ' ').capitalize(), 1, None))
    for category in sorted(by_category, key=lambda c: caption_of(c)[0].lower()):
        caption, icon, header = caption_of(category)
        members = sorted(by_category[category], key=order)
        if len(members) <= STAFF_FOLDER_SIZE:
            page(root, caption, icon, header, members, ['Staff', caption])
            continue
        folder = page(root, caption, icon, header, [], ['Staff', caption], folder=True)
        lines = {}
        for item in members:
            lines.setdefault(item['line'], []).append(item)
        small = []
        for line in sorted(lines, key=str.lower):
            if len(lines[line]) >= STAFF_SMALL_LINE and line:
                page(folder, line.replace('_', ' ').capitalize(), icon, header, lines[line], ['Staff', caption, line])
            else:
                small.append(line)
        bucket = []
        for line in small + [None]:
            if bucket and (line is None or sum(len(lines[l]) for l in bucket) + len(lines[line]) > STAFF_FOLDER_SIZE):
                first, last = (bucket[0][:1] or '#').upper(), (bucket[-1][:1] or '#').upper()
                label = f'Other lines {first}' if first == last else f'Other lines {first}–{last}'
                page(folder, label, icon, header, sorted((i for l in bucket for i in lines[l]), key=order), ['Staff', caption, label])
                bucket = []
            if line is not None:
                bucket.append(line)
    if custom:
        plus = [dict(kind=row['type'], classname=row['item_name'], name=row['item_name'], line='', category='custom', target=row['id'],
                     offer_id=STAFF_CUSTOM_OFFER_BASE + rank) for rank, row in enumerate(sorted(custom, key=lambda r: r['id']), 1)]
        page(root, 'Plus custom', 1, None, sorted(plus, key=order), ['Staff', 'Plus custom'])
    return tables, report


def builders_club_sentinel(offer):
    return offer['priceInCredits'] >= 10000 or offer['priceInActivityPoints'] >= 10000


def offer_price(offer):
    return (offer['priceInCredits'], max(offer['priceInActivityPoints'], 0), offer['activityPointType'] if offer['priceInActivityPoints'] > 0 else 0)


def iter_nodes(node):
    yield node
    for child in node['children']:
        yield from iter_nodes(child)


def capture_classnames(catalog):
    """(kind, furniClassId) of every furni product in the capture, club gifts included."""
    offers = [offer for page in catalog['pages'].values() for offer in page['offers']] + (catalog.get('clubGifts') or {}).get('offers', [])
    for offer in offers:
        for product in offer['products']:
            if product['productType'] in ('s', 'i'):
                yield product['productType'], product['furniClassId']


def plan_catalog(catalog, source_names, resolve, snapshot, allow_missing_pages=False):
    """Desired catalogue rows from the parsed capture. source_names: {(kind, class id): classname} of the captured hotel;
    resolve(kind, classname) -> furniture id, a ('new', kind, classname) placeholder or None."""
    pages_in = {int(page_id): page for page_id, page in catalog['pages'].items()}
    desired = {table: {} for table in CATALOG_TABLES}
    report = {'missing_pages': [], 'duplicate_pages': [], 'folders': [], 'duplicate_links': [], 'dropped_offers': [],
              'conflicting_offers': [], 'kept_offers': [], 'ignored': {'rent': 0, 'silver_priced': 0, 'not_giftable': 0, 'points_minus_one': 0, 'pet_offers': 0}}
    bots = {row['figure']: row['id'] for row in sorted(snapshot['catalog_bot_presets'], key=lambda r: r['id'], reverse=True)}
    habbicons = {row['id'] for row in snapshot['habbicons']}
    limited_now = {row['offer_id']: row for row in snapshot['catalog_offer_limited']}
    badges = set()
    links = set()
    synthetic = [FOLDER_PAGE_ID_BASE]
    seen = set()

    def put(table, row):
        key = tuple(row[column] for column in CATALOG_TABLES[table][0])
        desired[table][key] = row

    def offer_rows(offer):
        if offer['rent']:
            report['ignored']['rent'] += 1
            return None, 'rent offer'
        if offer['priceInSilver']:
            report['ignored']['silver_priced'] += 1
        if not offer['giftable']:
            report['ignored']['not_giftable'] += 1
        points = offer['priceInActivityPoints']
        if points == -1:
            # habbo.com prices some credit-only offers at -1 activity points.
            report['ignored']['points_minus_one'] += 1
            points = 0
        if offer['offerId'] <= 0 or not 0 <= offer['clubLevel'] <= 2 or min(offer['priceInCredits'], points, offer['activityPointType']) < 0:
            return None, 'offer id, club level or price out of range'
        if len(offer['localizationId']) > 100 or len(offer['previewImage']) > 255:
            return None, 'localization or preview image too long'
        products, ltd = [], None
        pet = PET_OFFER.fullmatch(offer['localizationId'])
        if pet and len(offer['products']) == 1:
            # habbo.com sells a pet as "a0 pet<type>" with a pet food furni as its picture; PlusEMU sells the pet itself.
            report['ignored']['pet_offers'] += 1
            offer = dict(offer, products=[dict(offer['products'][0], productType='p', furniClassId=int(pet[1]), extraParam='', productCount=1)])
        for position, product in enumerate(offer['products']):
            kind, class_id, amount = product['productType'], product['furniClassId'], product['productCount']
            row = dict(offer_id=offer['offerId'], position=position, product_type=None, amount=max(amount, 1),
                       extra_param=product['extraParam'], **{target: None for target in PRODUCT_TARGETS})
            if kind in ('s', 'i'):
                classname = source_names.get((kind, class_id))
                furniture_id = resolve(kind, classname) if classname else None
                if furniture_id is None:
                    return None, f'furni {kind}:{class_id} {classname or "(not in furnidata)"} has no definition'
                row.update(product_type='furni', furniture_id=furniture_id)
            elif kind == 'e':
                row.update(product_type='effect', effect_id=class_id, extra_param='')
            elif kind == 'b':
                if not product['extraParam'] or len(product['extraParam']) > 35:
                    return None, 'badge code missing or too long'
                row.update(product_type='badge', badge_code=product['extraParam'], amount=1, extra_param='')
                badges.add(product['extraParam'])
            elif kind == 'p':
                row.update(product_type='pet', pet_type=class_id, extra_param='')
            elif kind == 'r':
                if product['extraParam'] not in bots:
                    return None, 'bot figure matches no catalog_bot_presets row'
                row.update(product_type='bot', bot_preset_id=bots[product['extraParam']], extra_param='')
            elif kind == 'habbicon':
                if class_id not in habbicons:
                    return None, f'habbicon {class_id} does not exist'
                row.update(product_type='habbicon', habbicon_id=class_id, amount=1, extra_param='')
            else:
                return None, f'product type {kind!r} is not supported'
            if not 1 <= row['amount'] <= 1000 or len(row['extra_param']) > 1024:
                return None, 'product amount or extra parameter out of range'
            products.append(row)
            if product['uniqueLimitedItem'] and ltd is None:
                stack = product['uniqueLimitedItemSeriesSize']
                if stack <= 0:
                    return None, 'limited edition without a series size'
                # This hotel sells its own series: sales already made here are kept, habbo.com's are not copied.
                ltd = dict(offer_id=offer['offerId'], stack=stack, sold=min(limited_now.get(offer['offerId'], {}).get('sold', 0), stack))
        if not products:
            return None, 'no products'
        return (dict(id=offer['offerId'], localization_key=offer['localizationId'], cost_credits=offer['priceInCredits'],
                     cost_points=points, points_type=offer['activityPointType'], club_level=offer['clubLevel'],
                     bulk_purchase=int(offer['bundlePurchaseAllowed']), enabled=1, preview_image=offer['previewImage']), products, ltd), None

    def place(row_id, page_id, offer, position):
        """Puts an offer on a page; False when it cannot be sold here."""
        built, reason = offer_rows(offer)
        if built is None:
            report['dropped_offers'].append({'offerId': offer['offerId'], 'pageId': page_id, 'name': offer['localizationId'], 'reason': reason})
            return False
        offer_row, products, ltd = built
        if (offer_row['id'],) in desired['catalog_offers']:
            if desired['catalog_offers'][(offer_row['id'],)] != offer_row:
                report['conflicting_offers'].append({'offerId': offer_row['id'], 'pageId': page_id})
        else:
            put('catalog_offers', offer_row)
            for product in products:
                put('catalog_offer_products', product)
            if ltd:
                put('catalog_offer_limited', ltd)
        if (row_id, offer_row['id']) not in desired['catalog_page_offers']:
            put('catalog_page_offers', dict(page_id=row_id, offer_id=offer_row['id'], position=position))
        return True

    def visit(node, parent_id, position, depth):
        page_id = node['pageId']
        content = pages_in.get(page_id) if page_id > 0 else None
        if page_id > 0 and page_id not in seen:
            seen.add(page_id)
            row_id = page_id
        else:
            synthetic[0] += 1
            row_id = synthetic[0]
            (report['duplicate_pages'] if page_id > 0 else report['folders']).append({'pageId': page_id, 'id': row_id, 'name': node['pageName']})
        if page_id > 0 and content is None:
            report['missing_pages'].append({'pageId': page_id, 'name': node['pageName']})
        link = (node['pageName'] or '').strip()[:128] or None
        if link and link_key(link) in links:
            report['duplicate_links'].append({'id': row_id, 'link': link})
            link = None
        if link:
            links.add(link_key(link))
        put('catalog_pages', dict(id=row_id, parent_id=parent_id, link=link, caption=node['localization'][:128],
                                  layout=(content or {}).get('layoutCode') or 'default_3x3', required_permission=None,
                                  visible=int(node['visible']), enabled=int(content is not None), icon=node['icon'], required_club_level=0,
                                  position=position))
        if content:
            for slot, image in enumerate(content['images']):
                put('catalog_page_images', dict(page_id=row_id, slot=slot, image=image[:255]))
            for slot, text in enumerate(content['texts']):
                put('catalog_page_texts', dict(page_id=row_id, slot=slot, text=text))
            for offer_position, offer in enumerate(content['offers']):
                place(row_id, page_id, offer, offer_position)
        if depth < 64:
            for child_position, child in enumerate(node['children']):
                visit(child, row_id, child_position, depth + 1)

    for position, node in enumerate(catalog['index']['children']):
        visit(node, None, position, 1)
    if report['missing_pages'] and not allow_missing_pages:
        raise ValueError(f"{len(report['missing_pages'])} index pages were not captured; re-capture or pass --allow-missing-pages.")

    club_gifts = catalog.get('clubGifts')
    report['club_gifts'] = {'captured': club_gifts is not None}
    if club_gifts is not None:
        # Habbo's HC gifts: their offers sit on one hidden page, as PlusEMU offers gifts only from pages a member can open.
        put('catalog_pages', dict(id=CLUB_GIFT_PAGE_ID, parent_id=None, link=None, caption='Club gifts', layout='default_3x3',
                                  required_permission=None, visible=0, enabled=1, icon=0, required_club_level=0,
                                  position=len(catalog['index']['children'])))
        placed = {offer['offerId'] for position, offer in enumerate(club_gifts['offers']) if place(CLUB_GIFT_PAGE_ID, None, offer, position)}
        gift_data = [gift for gift in club_gifts['giftData'] if gift['offerId'] in placed]
        for gift in gift_data:
            put('club_gift_offers', dict(offer_id=gift['offerId'], days_required=max(gift['daysRequired'], 0), enabled=1))
        report['club_gifts'].update(offers=len(placed), gifts=len(gift_data), vip=sum(gift['isVip'] for gift in gift_data),
                                    without_offer=[gift['offerId'] for gift in club_gifts['giftData'] if gift['offerId'] not in placed])
    else:
        # Without gift data the configured gifts stay, and so do their offers.
        for row in snapshot['club_gift_offers']:
            put('club_gift_offers', {column: row[column] for column in CATALOG_TABLES['club_gift_offers'][1]})
        kept = {row['offer_id'] for row in snapshot['club_gift_offers']} - {key[0] for key in desired['catalog_offers']}
        for table in ('catalog_offers', 'catalog_offer_products', 'catalog_offer_limited'):
            id_column = 'id' if table == 'catalog_offers' else 'offer_id'
            for row in snapshot[table]:
                if row[id_column] in kept:
                    put(table, {column: row[column] for column in CATALOG_TABLES[table][1]})
    gifts = {key[0] for key in desired['club_gift_offers']}
    report['kept_offers'] = sorted(offer_id for offer_id in gifts if not any(key[1] == offer_id for key in desired['catalog_page_offers']))

    promotions = []
    for node_page in sorted(desired['catalog_pages'].values(), key=lambda row: row['id']):
        items = (pages_in.get(node_page['id']) or {}).get('frontPageItems') or []
        if items:
            promotions = [promotion_row(item, catalog.get('source', {}).get('capturedAt')) for item in items]
            break
    return {'tables': desired, 'badges': badges, 'promotions': promotions, 'report': report}


def promotion_row(item, captured_at):
    expires = None
    if item.get('secondsToExpiration', 0) > 0 and captured_at:
        moment = datetime.fromisoformat(captured_at.replace('Z', '+00:00')).replace(tzinfo=None)
        expires = (moment + timedelta(seconds=item['secondsToExpiration'])).strftime('%Y-%m-%d %H:%M:%S.%f')
    return dict(title=latin1(item['itemName'], 128), image=latin1(item['itemPromoImage'], 255),
                page_link=latin1(item.get('cataloguePageLocation') or '', 128), position=item['position'],
                item_type=item['type'], offer_id=item['productOfferId'] if item.get('productOfferId') is not None else -1,
                product_code=latin1(item.get('productCode') or '', 128), expires_at=expires)


# ---- differences and SQL --------------------------------------------------

def diff_table(table, current_rows, desired_rows):
    keys, columns = CATALOG_TABLES[table]
    current = {tuple(row[column] for column in keys): row for row in current_rows}
    inserts = [row for key, row in desired_rows.items() if key not in current]
    updates = []
    for key, row in desired_rows.items():
        if key in current:
            changed = {column: row[column] for column in columns if column not in keys and not same(current[key][column], row[column])}
            if changed:
                updates.append((key, changed))
    deletes = [key for key in current if key not in desired_rows]
    return {'insert': inserts, 'update': updates, 'delete': deletes}


def literal(value):
    if value is None:
        return 'NULL'
    if isinstance(value, tuple):
        return (f"(SELECT `id` FROM `furniture` WHERE `type` = {literal(value[1])} AND `furnidata_classname` = "
                f"{literal(value[2])} AND `item_name` = BINARY {literal(value[2])})")
    if isinstance(value, str):
        return 'CONVERT(0x' + value.encode().hex() + ' USING utf8mb4)' if value else "''"
    if isinstance(value, bool):
        return str(int(value))
    return repr(value) if isinstance(value, float) else str(value)


def where(keys, key):
    return ' AND '.join(f'`{column}` = {literal(value)}' for column, value in zip(keys, key))


def assignments(changes):
    return ', '.join(f'`{column}` = {literal(value)}' for column, value in changes.items())


def insert(table, row):
    return f'INSERT INTO `{table}` (' + ', '.join(f'`{column}`' for column in row) + ') VALUES (' + ', '.join(literal(v) for v in row.values()) + ');'


def plan(snapshot, catalog, habbo_furnidata, source_furnidata, evidence, assets=None, allow_missing_pages=False):
    """Everything apply would write, and the report."""
    lines = {(kind, entry['id']): entry.get('furniline') for kind, entry in furnidata_entries(source_furnidata)}
    catalog, builders_club = merge_builders_club(catalog, lambda kind, class_id: lines.get((kind, class_id)))
    habbo = {(kind, entry['classname']): entry for kind, entry in furnidata_entries(habbo_furnidata)}
    source = {(kind, entry['id']): entry['classname'] for kind, entry in furnidata_entries(source_furnidata)}
    sold = {(kind, source[(kind, class_id)]) for kind, class_id in capture_classnames(catalog) if (kind, class_id) in source}
    # Every Habbo furnidata entry gets a definition, sold or not (rares, LTDs, retired furni).
    needed = sold | set(habbo)
    furniture = plan_furniture(snapshot['furniture'], habbo, needed, evidence)
    furniture['report']['created_unsold'] = sum((row['type'], row['item_name']) not in sold for row in furniture['inserts'])
    by_name = {}
    for row in sorted(snapshot['furniture'], key=lambda r: (not r['has_furnidata'], r['id'])):
        by_name.setdefault((row['type'], row['item_name']), row['id'])

    def resolve(kind, classname):
        return furniture['owner_of'].get((kind, classname), by_name.get((kind, classname)))

    catalog_plan = plan_catalog(catalog, source, resolve, snapshot, allow_missing_pages)
    # An offer that already sells a Plus duplicate of the same furni (another row with its classname) keeps it; furnidata
    # names offers by classname, so a row of another name would lose the offer id.
    final = {row['id']: (row['type'], furniture['updates'].get(row['id'], {}).get('item_name', row['item_name'])) for row in snapshot['furniture']}
    current_products = {(row['offer_id'], row['position']): row['furniture_id'] for row in snapshot['catalog_offer_products'] if row['furniture_id']}
    for key, product in catalog_plan['tables']['catalog_offer_products'].items():
        current, wanted = current_products.get(key), product['furniture_id']
        name = wanted[1:] if isinstance(wanted, tuple) else final.get(wanted)
        if current is not None and current != wanted and final.get(current) == name:
            product['furniture_id'] = current
    # Staff tab: what no public page sells.
    public = catalog_plan['tables']
    names = {row['id']: (row['type'], furniture['updates'].get(row['id'], {}).get('item_name', row['item_name'])) for row in snapshot['furniture']}
    sold = {(target[1], target[2]) if isinstance(target, tuple) else names.get(target)
            for target in (row['furniture_id'] for row in public['catalog_offer_products'].values() if row['product_type'] == 'furni')}
    unsold = [dict(entry, kind=kind, target=resolve(kind, classname)) for (kind, classname), entry in sorted(habbo.items())
              if (kind, classname) not in sold]
    classnames = {classname for _, classname in habbo}
    custom = [dict(row, item_name=names[row['id']][1]) for row in sorted(snapshot['furniture'], key=lambda r: r['id'])
              if row['type'] in ('s', 'i') and names[row['id']][1] not in classnames]
    links = {link_key(row['link']) for row in public['catalog_pages'].values() if row['link']}
    staff, staff_report = staff_tree(unsold, custom, sum(1 for row in public['catalog_pages'].values() if row['parent_id'] is None),
                                     lambda link: link_key(link) in links)
    for table, rows in staff.items():
        clash = set(rows) & set(public[table])
        if clash:
            raise ValueError(f'Staff tab collides with the public catalogue in {table}: {sorted(clash)[:5]}')
        public[table].update(rows)
    catalog_plan['report']['staff'] = {'pages': staff_report, 'unsold_habbo_furni': len(unsold), 'plus_custom': len(custom),
                                       'large_pages': [page for page in staff_report if page['furni'] > STAFF_FOLDER_SIZE]}
    with_products = {key[0] for key in catalog_plan['tables']['catalog_offer_products']}
    empty = [key[0] for key in catalog_plan['tables']['catalog_offers'] if key[0] not in with_products]
    if empty:
        raise ValueError(f'Offers without products would be written: {empty[:10]}')
    tables = {table: diff_table(table, snapshot[table], catalog_plan['tables'][table]) for table in CATALOG_TABLES}
    current_promotions = sorted((tuple(str(row[c]) if c == 'expires_at' and row[c] is not None else row[c] for c in PROMOTION_COLUMNS)
                                 for row in snapshot['catalog_promotions']), key=repr)
    wanted_promotions = sorted((tuple(row[c] for c in PROMOTION_COLUMNS) for row in catalog_plan['promotions']), key=repr)
    promotions = catalog_plan['promotions'] if catalog_plan['promotions'] and current_promotions != wanted_promotions else None
    known_badges = {row['code'].lower() for row in snapshot['badge_definitions']}
    badges = sorted(code for code in catalog_plan['badges'] if code.lower() not in known_badges)
    result = {'furniture': furniture, 'catalog': catalog_plan, 'tables': tables, 'promotions': promotions, 'badges': badges}
    result['report'] = build_report(result, snapshot, catalog, habbo, assets)
    result['report']['builders_club'] = builders_club
    return result


def changes(result):
    """Number of writes a plan needs; 0 means apply has nothing to do."""
    return (len(result['furniture']['updates']) + len(result['furniture']['inserts']) + len(result['badges'])
            + (len(result['promotions']) + 1 if result['promotions'] else 0)
            + sum(len(diff['insert']) + len(diff['update']) + len(diff['delete']) for diff in result['tables'].values()))


def statements(result):
    sql = []
    furniture = result['furniture']
    # Owners whose sprite or classname changes leave the unique furnidata keys first, so ids can move in any order.
    updates = furniture['updates']
    freed = sorted(row_id for row_id, columns in updates.items() if 'has_furnidata' in columns)
    for chunk in range(0, len(freed), 500):
        sql.append('UPDATE `furniture` SET `has_furnidata` = FALSE WHERE `id` IN (' + ','.join(map(str, freed[chunk:chunk + 500])) + ');')
    for row_id, columns in sorted(updates.items()):
        sql.append(f'UPDATE `furniture` SET {assignments(columns)} WHERE `id` = {row_id};')
    for row in furniture['inserts']:
        sql.append(insert('furniture', {column: value for column, value in row.items() if value is not None or column in FURNIDATA_COLUMNS}))
    for code in result['badges']:
        sql.append(insert('badge_definitions', {'code': code}))

    tables = result['tables']
    for table in ('club_gift_offers', 'catalog_page_offers', 'catalog_offer_limited', 'catalog_offer_products', 'catalog_page_images',
                  'catalog_page_texts'):
        keys = CATALOG_TABLES[table][0]
        sql += [f'DELETE FROM `{table}` WHERE {where(keys, key)};' for key in tables[table]['delete']]
    sql += [f'DELETE FROM `catalog_offers` WHERE `id` = {key[0]};' for key in tables['catalog_offers']['delete']]
    pages = tables['catalog_pages']
    sql += [insert('catalog_pages', dict(row, parent_id=None, link=None)) for row in pages['insert']]
    # Links are unique: pages that change or lose theirs release them before any page takes one.
    relinked = [key for key, columns in pages['update'] if 'link' in columns] + pages['delete']
    sql += [f'UPDATE `catalog_pages` SET `link` = NULL WHERE `id` = {key[0]};' for key in relinked]
    sql += [f"UPDATE `catalog_pages` SET {assignments(columns)} WHERE `id` = {key[0]};" for key, columns in pages['update']]
    sql += [f"UPDATE `catalog_pages` SET `parent_id` = {literal(row['parent_id'])}, `link` = {literal(row['link'])} WHERE `id` = {row['id']};"
            for row in pages['insert'] if row['parent_id'] is not None or row['link'] is not None]
    sql += [f'UPDATE `catalog_pages` SET `parent_id` = NULL WHERE `id` = {key[0]};' for key in pages['delete']]
    sql += [f'DELETE FROM `catalog_pages` WHERE `id` = {key[0]};' for key in pages['delete']]
    for table in ('catalog_offers', 'catalog_offer_products', 'catalog_offer_limited', 'catalog_page_offers', 'catalog_page_images',
                  'catalog_page_texts', 'club_gift_offers'):
        keys = CATALOG_TABLES[table][0]
        sql += [insert(table, row) for row in tables[table]['insert']]
        sql += [f'UPDATE `{table}` SET {assignments(columns)} WHERE {where(keys, key)};' for key, columns in tables[table]['update']]
    if result['promotions']:
        sql.append('DELETE FROM `catalog_promotions`;')
        sql += [insert('catalog_promotions', row) for row in result['promotions']]
    # Open marketplace listings name the sprite they show.
    if any('sprite_id' in columns for columns in updates.values()):
        sql.append('UPDATE `catalog_marketplace_offers` AS `offer` INNER JOIN `furniture` ON `furniture`.`id` = `offer`.`furni_id` '
                   'SET `offer`.`sprite_id` = `furniture`.`sprite_id` WHERE `offer`.`sprite_id` <> `furniture`.`sprite_id`;')
    return sql


# ---- report ---------------------------------------------------------------

def build_report(result, snapshot, catalog, habbo, assets):
    tables = result['tables']
    furniture = result['furniture']['report']
    desired = result['catalog']['tables']
    offers = desired['catalog_offers'].values()
    summary = {table: {action: len(diff[action]) for action in ('insert', 'update', 'delete')} for table, diff in tables.items()}
    currencies = Counter(offer['points_type'] for offer in offers if offer['cost_points'] > 0)
    removed_offers = {key[0] for key in tables['catalog_offers']['delete']}
    removed_pages = {key[0] for key in tables['catalog_pages']['delete']}
    admin = Counter('offer' if row['entity_type'] == 'OFFER' and row['entity_id'] in removed_offers else
                    'page' if row['entity_type'] == 'PAGE' and row['entity_id'] in removed_pages else 'kept'
                    for row in snapshot['catalog_admin_log'])
    offer_ids = {key[0] for key in desired['catalog_offers']}
    links = {link_key(row['link']) for row in desired['catalog_pages'].values() if row['link']}
    promotions = result['catalog']['promotions'] or snapshot['catalog_promotions']
    dangling = [row for row in promotions if (row['page_link'] and link_key(row['page_link']) not in links)
                or (row['offer_id'] not in (-1, 0) and row['offer_id'] not in offer_ids)]
    report = {
        'source': catalog.get('source'),
        'counts': {
            'pages': summary['catalog_pages'], 'page_images': summary['catalog_page_images'], 'page_texts': summary['catalog_page_texts'],
            'offers': summary['catalog_offers'], 'products': summary['catalog_offer_products'], 'page_offers': summary['catalog_page_offers'],
            'limited': summary['catalog_offer_limited'], 'promotions_replaced': len(result['promotions'] or []),
            'badges_defined': len(result['badges']),
            'furniture_created': len(furniture['created']), 'furniture_created_unsold': furniture['created_unsold'], 'furniture_updated': len(result['furniture']['updates']),
            'sprite_moves': len(furniture['sprite_moves']), 'sprite_collisions': len(furniture['sprite_collisions']),
            'renamed': len(furniture['renamed']), 'stacking_turned_off': furniture['stacking_turned_off'], 'interactions_derived': len(furniture['derived']),
            'left_default_with_hint': len(furniture['left_default_with_hint']), 'writes': changes(result)},
        'catalog': result['catalog']['report'],
        'currencies': {str(points_type): count for points_type, count in sorted(currencies.items())},
        'references': {
            'club_gift_offers': {'rows': len(snapshot['club_gift_offers']), **result['catalog']['report']['club_gifts'],
                                 'offers_kept_off_pages': result['catalog']['report']['kept_offers']},
            'club_gift_claims': {'rows': len(snapshot['club_gift_claims']), 'note': 'history; offer ids are kept as they were'},
            'catalog_admin_log': {'rows': len(snapshot['catalog_admin_log']), 'naming_removed_offers': admin['offer'],
                                  'naming_removed_pages': admin['page'], 'note': 'history; one IMPORT row is added per apply'},
            'catalog_promotions': {'replaced': bool(result['promotions']), 'dangling': dangling},
            'catalog_marketplace_offers': 'sprite_id follows furniture.sprite_id of furni_id',
            'items.base_item': 'furniture ids never change'},
        'furniture': furniture,
        'votes': {'arcturus': 'catalog.sql items_base', 'habbobba': 'not available locally; not used'},
        'interactions': dict(Counter(row['interaction'] for row in furniture['derived']).most_common()),
        'interaction_rules': dict(Counter(row['rule'].split('=')[0] for row in furniture['derived']).most_common()),
        'hardcoded_sprites': hardcoded_sprites(result, snapshot),
    }
    report['assets'] = missing_assets(result, snapshot, habbo, assets)
    return report


# Sprite ids the emulator names in code: the recycler box and the ten gift wrappings it offers.
HARDCODED_SPRITES = {('s', 3095): r'ecotron_box', **{('s', 3372 + n): r'present_wrap\*[0-9]+' for n in range(10)}}


def hardcoded_sprites(result, snapshot):
    final = {}
    for row in snapshot['furniture']:
        update = result['furniture']['updates'].get(row['id'], {})
        if update.get('has_furnidata', row['has_furnidata']):
            final[(row['type'], update.get('sprite_id', row['sprite_id']))] = update.get('item_name', row['item_name'])
    for row in result['furniture']['inserts']:
        final[(row['type'], row['sprite_id'])] = row['item_name']
    return [{'type': kind, 'sprite_id': sprite, 'expected': name, 'actual': final.get((kind, sprite))}
            for (kind, sprite), name in HARDCODED_SPRITES.items() if not re.fullmatch(name, final.get((kind, sprite)) or '')]


def missing_assets(result, snapshot, habbo, assets):
    """Classnames the new catalogue sells (and every Habbo-owned row) whose .hab or icon is not on R2."""
    if not assets:
        return {'checked': False, 'missing': []}
    habs, icons = assets
    updates = result['furniture']['updates']
    names = {(row['type'], updates.get(row['id'], {}).get('item_name', row['item_name'])) for row in snapshot['furniture']
             if updates.get(row['id'], {}).get('has_furnidata', row['has_furnidata'])}
    names |= {(row['type'], row['item_name']) for row in result['furniture']['inserts']}
    missing = []
    for kind, classname in sorted(names):
        entry = habbo.get((kind, classname))
        if entry is None:
            continue
        hab = library(classname) + '.hab' not in habs
        icon = icon_name(classname) not in icons
        if hab or icon:
            missing.append({'classname': classname, 'type': kind, 'library': library(classname), 'revision': entry.get('revision') or 0,
                            'hab': hab, 'icon': icon_name(classname) if icon else None})
    return {'checked': True, 'missing': missing, 'missing_hab': sum(m['hab'] for m in missing), 'missing_icon': sum(bool(m['icon']) for m in missing)}


# ---- database -------------------------------------------------------------

class Database:
    """One mariadb session in the database container; rows come back as JSON lines."""

    def __init__(self, container, database=None):
        name = f"'{database}'" if database else '"$MARIADB_DATABASE"'
        self.process = subprocess.Popen(
            ['docker', 'exec', '-i', container, 'sh', '-c',
             f'MYSQL_PWD="$MARIADB_ROOT_PASSWORD" exec mariadb -uroot --batch --raw --skip-column-names --unbuffered {name}'],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')

    def query(self, sql):
        self.process.stdin.write(sql + "\nSELECT '{\"end\":true}';\n")
        self.process.stdin.flush()
        rows = []
        while True:
            line = self.process.stdout.readline()
            if not line:
                raise ValueError('Database command failed; the transaction is rolled back on disconnect: ' + self.process.stderr.read()[-2000:])
            row = json.loads(line)
            if row == {'end': True}:
                return rows
            rows.append(row)

    def close(self):
        self.process.stdin.close()
        self.process.wait(timeout=60)
        self.process.stdout.close()
        self.process.stderr.close()


SNAPSHOT = {
    'furniture': FURNITURE_COLUMNS,
    **{table: columns for table, (_, columns) in CATALOG_TABLES.items()},
    'catalog_promotions': ['id'] + PROMOTION_COLUMNS,
    'club_gift_claims': ['offer_id'], 'catalog_admin_log': ['entity_type', 'entity_id'],
    'catalog_bot_presets': ['id', 'figure'], 'habbicons': ['id'],
    'badge_definitions': ['code'],
}
LOCKED = {'furniture', 'catalog_promotions', 'badge_definitions', *CATALOG_TABLES}


def read_snapshot(db, lock=False):
    snapshot = {}
    for table, columns in SNAPSHOT.items():
        pairs = ','.join(f"'{column}',`{column}`" for column in columns)
        suffix = ' FOR UPDATE' if lock and table in LOCKED else ''
        snapshot[table] = db.query(f'SELECT JSON_OBJECT({pairs}) FROM `{table}`{suffix};')
    for row in snapshot['furniture']:
        row['type'] = row['type'].lower()
    return snapshot


# ---- command line ---------------------------------------------------------

def load_evidence(args):
    habs = {}
    if args.hab_cache:
        for path in sorted(Path(args.hab_cache).glob('*.hab')):
            try:
                habs[path.stem] = hab_logic(path.read_bytes())
            except (ValueError, KeyError, zlib.error, json.JSONDecodeError, StopIteration):
                continue
    refs = {}
    for spec in args.reference or []:
        name, _, path = spec.partition('=')
        refs[name] = reference_votes(path)
    return Evidence(habs, refs, wired_box_names())


def summary_line(report):
    counts = report['counts']
    return json.dumps({key: counts[key] for key in ('pages', 'offers', 'products', 'page_offers', 'furniture_created', 'furniture_updated',
                                                     'sprite_moves', 'sprite_collisions', 'interactions_derived', 'left_default_with_hint',
                                                     'writes')} | {'currencies': report['currencies'],
                                                                    'missing_assets': len(report['assets']['missing'])})


def run(args):
    if args.command == 'fetch-habs':
        print(json.dumps(fetch_habs(load_json(args.furnidata), Path(args.cache), args.workers)))
        return
    if args.command == 'fetch-assets':
        print(json.dumps(fetch_assets(load_json(args.report), Path(args.out), args.workers)))
        return
    catalog = load_json(args.catalog)
    habbo_furnidata = load_json(args.furnidata)
    source_furnidata = load_json(args.source_furnidata) if args.source_furnidata else habbo_furnidata
    evidence = load_evidence(args)
    assets = (listing(args.r2_furniture), listing(args.r2_icons)) if args.r2_furniture and args.r2_icons else None
    unknown = derivable_interactions() - interaction_names()
    if unknown:
        raise ValueError('Interaction names the emulator does not parse: ' + ', '.join(sorted(unknown)))
    db = Database(args.container, args.database)
    try:
        if args.command == 'apply':
            if db.query(f"SELECT JSON_OBJECT('acquired', GET_LOCK('{LOCK_NAME}', 10));") != [{'acquired': 1}]:
                raise ValueError('Import lock unavailable.')
            db.query('SET SESSION innodb_lock_wait_timeout = 30; START TRANSACTION;')
        else:
            db.query('START TRANSACTION READ ONLY;')
        snapshot = read_snapshot(db, args.command == 'apply')
        result = plan(snapshot, catalog, habbo_furnidata, source_furnidata, evidence, assets, args.allow_missing_pages)
        if args.report:
            Path(args.report).write_text(json.dumps(result['report'], indent=1, ensure_ascii=False) + '\n')
        if args.command == 'apply' and changes(result):
            sql = statements(result)
            counts = result['report']['counts']
            sql.append("INSERT INTO `catalog_admin_log` (`user_id`, `username`, `action`, `entity_type`, `catalog_type`, `entity_id`, "
                       "`operation`, `summary`) VALUES (0, 'habbo-import', 'habbo_catalog_import', 'PAGE', 'NORMAL', 0, 'IMPORT', "
                       + literal(f"habbo.com catalogue import: {counts['writes']} changes"[:255]) + ');')
            for chunk in range(0, len(sql), 2000):
                db.query('\n'.join(sql[chunk:chunk + 2000]))
            after = plan(read_snapshot(db, True), catalog, habbo_furnidata, source_furnidata, evidence, None, args.allow_missing_pages)
            if changes(after):
                raise ValueError(f'Post-import check found {changes(after)} differences left; rolled back.')
            db.query('COMMIT;')
        else:
            db.query('ROLLBACK;')
        print(json.dumps({'mode': args.command, 'applied': args.command == 'apply' and changes(result) > 0}) + '\n' + summary_line(result['report']))
    finally:
        db.close()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest='command', required=True)
    for name in ('plan', 'apply'):
        command = commands.add_parser(name)
        command.add_argument('--catalog', required=True, help='catalog.json from capture/parse.py')
        command.add_argument('--furnidata', required=True, help="Habbo's furnidata.json")
        command.add_argument('--source-furnidata', help="furnidata of the captured hotel when it is not habbo.com (its class ids)")
        command.add_argument('--hab-cache', help='folder of official .hab bundles (fetch-habs)')
        command.add_argument('--reference', action='append', metavar='NAME=PATH',
                             help='an emulator dump with items_base (Arcturus MS 3.5.5, catalog.sql, Polaris) for interaction references')
        command.add_argument('--r2-furniture', help='rclone lsf of plusemu-assets/assets/furniture/')
        command.add_argument('--r2-icons', help='rclone lsf of plusemu-assets/c_images/hof_furni/icons/')
        command.add_argument('--container', required=True, help='MariaDB container (credentials stay in the container)')
        command.add_argument('--database', help='database name; default $MARIADB_DATABASE in the container')
        command.add_argument('--report', help='write the JSON report here')
        command.add_argument('--allow-missing-pages', action='store_true', help='keep index pages the capture lacks as headings')
    command = commands.add_parser('fetch-habs')
    command.add_argument('--furnidata', required=True)
    command.add_argument('--cache', required=True)
    command.add_argument('--workers', type=int, default=8)
    command = commands.add_parser('fetch-assets')
    command.add_argument('--report', required=True, help='plan report listing missing assets')
    command.add_argument('--out', required=True, help='staging folder (assets/furniture, c_images/hof_furni/icons)')
    command.add_argument('--workers', type=int, default=8)
    run(parser.parse_args(argv))


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError, subprocess.SubprocessError) as error:
        sys.exit(str(error))
