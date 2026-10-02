# Additive Wired furniture and catalogue import

This is migration 16's explicit data operation. It adds definitions and a separate
`plus_recently_added_wired_v1` catalogue page, captioned **Recently Added**, using
database-allocated IDs. It never changes or removes existing furniture, offers, pages, placed items, inventories, settings,
wired configurations, or any of the 50 legacy `wired_id` meanings. New definitions
use `wired_id=0`; the integrated factory resolves their canonical item names.
Executing migration 16's SQL file alone intentionally does nothing.

## Evidence and limits

`manifest.json` covers the immutable registry `58a2ee4767379dc72e037cab4c061ff33066c0f5`
(172 canonical names), plus four clock/game counters and two antennas. The local
`nitro-assets/furniture/json/FurnitureData.json` supplies exact sprite IDs, dimensions,
default rotation and flags. Each matching `.nitro` bundle was inflated using the
native Nitro bundle format and checked for its own classname, PNG texture,
logic-model dimensions, height and directions. SHA-256 digests pin the reviewed inputs.

All 172 canonical names and six auxiliaries now have verified original assets. The
150 original local canonical entries and six local auxiliaries retain their reviewed
SpriteIds and bundles. The 22 additions came from the public
[HabboFurni Wired inventory](https://habbofurni.com/furniture?categoryFilter=wired),
with exact classname/revision searches and each item's public view and download links.
The documented API requires authentication; this retrieval uses its public website,
not an invented unauthenticated API endpoint. `original-assets.json` records source
URLs, revision, actual SpriteId evidence, metadata/SWF/icon/Nitro SHA-256 hashes and
the pinned converter commit/dependency lock.

**The website export ID is a website record ID, not a SpriteId.** The collector
rejects it and requires a unique public revision-history “Furni id”. Native SWF
SymbolClass/index/logic XML confirms the exact original classname, dimensions,
height, directions, logic and visualization. Converted bundle models must agree.
The active Octane-Renderer `OctaneBundle` parser and PNG decoder validate the 22 bundles,
22 icons and 363 texture frames; browser/GPU placement and editing remain preview
checks, not a claim made by catalogue data or constructor support.

Six canonical server names differ from their original client asset classnames:

| Canonical suffix after `wf_xtra_` | Original asset suffix |
| --- | --- |
| `var_fx_boss` | `varfx_boss` |
| `var_fx_health` | `varfx_hp` |
| `var_fx_level` | `varfx_levelling` |
| `var_fx_number` | `varfx_number` |
| `var_fx_progress` | `varfx_prog` |
| `var_fx_status` | `varfx_status` |

Polaris `ItemManager.java` at `34bc0d49511c659fd0054d35d957669ebd6ce9ed`
registers each pair to the same concrete interaction. The manifest preserves canonical
server item names and protocol/editor codes while `asset_classname` identifies the
original native bundle and client FurniData name. No bundle is renamed, no SpriteId is
invented, and no generic donor appearances are needed.

The private overlay contains only 22 new bundles and icons plus merged FurniData.
All 13,689 base floor entries, their ordering, wall entries and other metadata remain
unchanged; 22 original classname entries are appended. Only the additions normalize
HabboFurni’s absent `partcolors.color=null` to `[]`, the iterable empty list required
by Octane. Raw metadata receipts retain the source values. The actual active
`FurnitureDataLoader` loads all 13,711 floor entries and 704 wall entries, including
the 22 original sprite/classname/footprint/localization mappings. The importer requires both
`--assets` (read-only baseline) and `--asset-overlay` (reviewed private originals).
It verifies hashes, additions, baseline preservation and SpriteId collisions before
planning any offer. The overlay is not installed into the shared or live asset tree.

Heights and directions follow each native logic model rather than a blanket box
height. The database stores neither default direction nor an allowed-direction list;
those remain in the manifest and original client assets. Stacking is enabled for new
boxes; walkability and seating follow the verified FurniData flags.

## Factory support, counters and antennas

A registry entry alone never permits publication. The importer requires a reviewed
`factory-support.json` inventory from the compiled factory, covering all 172 names
exactly once with `Implemented` or `DescriptorOnly`. Its `engineCommit` is a full
immutable hash and `registryCommit` must match the manifest. Auxiliary support is
separate and must explicitly identify the exact interaction and working lifecycle.
The ledger is reviewed evidence; supplying JSON does not independently prove a
running hotel uses that engine. The expected engine hash on apply records the caller's
reviewed dependency, not a claim about deployed services.

Counter definitions use `wf_upcounter1` (13383), `wf_upcounter2` (13396),
`wf_game_upcounter1` (13395), and `wf_game_upcounter2` (13384) as both item name and
interaction. A generic `counter` interaction would mask the name in the controller's
recognition logic. Antennas `wf_antenna1` (14097) and `wf_antenna2` (14095) use the
exact interaction `antenna`, required by receiver validation. The engine owner must
confirm attach/use/tick/detach recognition before marking auxiliaries supported.

Existing definitions are reused by canonical name only after sprite/type checks.
Their geometry, interaction and legacy wired ID are reported unchanged. This depends
on the integrated `ItemDataManager.ReadInteractionType` and concrete factory resolving
canonical names even for old generic interactions. An unrelated owner of a normal
sprite is excluded; duplicate names, conflicting canonical sprites, page links, or
owned-offer data abort the transaction. No conflicting row is repaired automatically.

## Commands

Run from this worktree/repository root. Python uses only its standard library; Docker
and the already-running isolated preview database are required for online inventory.
No credential is read from a private file or printed: the DB container expands its
own `MARIADB_ROOT_PASSWORD` internally.

```sh
python3 scripts/import-wired-catalog.py \
  --assets /home/ubuntu/dev/plus/nitro-assets/furniture \
  --asset-overlay /home/ubuntu/dev/plus/plusemu-comparison/wired-original-assets-candidate/furniture \
  --support-ledger /path/to/reviewed-factory-support.json \
  --report /tmp/wired-plan.json

python3 scripts/check-wired-catalog.py \
  --assets /home/ubuntu/dev/plus/nitro-assets/furniture \
  --asset-overlay /home/ubuntu/dev/plus/plusemu-comparison/wired-original-assets-candidate/furniture \
  --support-ledger /path/to/reviewed-factory-support.json
```

The second command copies only the actual furniture/catalogue schema and data into
`plus-wired-catalog-check`, a temporary MariaDB container with no network, ports or
existing volumes. Its datadir uses tmpfs. It exercises inserts, committed reruns,
row/legacy-ID preservation, transaction rollback, disconnect rollback, ambiguous
identity/page conflicts and ID exhaustion. The container is removed even on failure.
It refuses to replace a pre-existing container with that name.

The default importer takes a read-only transaction and reports planned changes.
`--snapshot /path/inventory.json` supports an offline review; it cannot be combined
with apply. On explicit apply, the importer checks the exact container name,
`plus-wired-preview` project/service, `plus-wired-preview_wired-db-data` volume and
`plus-wired-preview_default` network. It serializes importers with an advisory lock,
locks the scanned records/gaps in a serializable transaction, rechecks idempotence
before committing, and rolls back on failure/disconnect. InnoDB is required.

After root reviews the artifact, and only with the ledger's reviewed engine hash:

```sh
python3 scripts/import-wired-catalog.py \
  --assets /home/ubuntu/dev/plus/nitro-assets/furniture \
  --asset-overlay /home/ubuntu/dev/plus/plusemu-comparison/wired-original-assets-candidate/furniture \
  --support-ledger /path/to/reviewed-factory-support.json \
  --expected-engine-commit FULL_REVIEWED_HASH \
  --apply-isolated --report /tmp/wired-applied-plan.json
```

There is no configurable live DB target or deployment step. Existing catalogue pages
and pricing remain unchanged. The new fixture offers cost zero, are ordinary single
items with server `offer_id=-1`, and reside in their own visible page. No
client-global offer mapping is rewritten.
Stop the emulator before fixture application and reload definitions/catalogue afterward
within the separately authorized preview workflow. This importer creates no room or
account and performs no service action. Retain the plan with allocated IDs from the
next read-only run if later fixture cleanup is needed; do not delete definitions while
placed/inventory items reference them.

## Reproduce the private original asset artifact

The collector targets only these 22 additions, including the six source-backed
original classnames. Use new output directories to retain prior provenance.

```sh
python3 scripts/fetch-wired-original-assets.py --output /path/to/new-originals
```

Clone `https://github.com/billsonnn/nitro-converter` into an isolated tool directory,
checkout `e0a1800a83feda5f9b1b5cfde7fed0181de7b06f`, inspect its package scripts, then
run `npx --yes yarn@1.22.22 install --frozen-lockfile` and
`./node_modules/.bin/tsc` there. The reviewed lock SHA-256 is
`ba5a8fd9da1dd844f5850c9f5dd6478dd4c245c8067cb4c8bc3cf5faa8adadb7`.
No global installation or system upgrade is required.

Create a private conversion working directory with only the collected SWFs under
`assets/swf/furniture/`, an empty local external-variable file, and
`configuration.json` containing `{"external.variables.url":"/absolute/path/to/empty-file"}`.
Run `node /path/to/nitro-converter/dist/Main.js --convert-swf` from that directory.
This mode reads only the supplied files; it does not bulk-download furniture. Then:

```sh
python3 scripts/build-wired-asset-overlay.py \
  --downloads /path/to/new-originals \
  --converted /path/to/conversion-run/assets/bundled/furniture \
  --converter /path/to/nitro-converter \
  --assets /home/ubuntu/dev/plus/nitro-assets/furniture \
  --output /path/to/new-private-overlay/furniture

node scripts/check-wired-nitro.cjs /path/to/nitro-converter \
  /path/to/octane-renderer /path/to/new-private-overlay/furniture
```

Assembly rejects altered source/dependencies, incomplete downloads, extra converted
bundles, ID/classname collisions, and native-model disagreement. It creates
`furniture/nitro/`, `furniture/icons/`, `furniture/json/FurnitureData.json` and
`furniture/original-assets.json`. The renderer checker transpiles the actual Octane
parser in memory using the isolated converter's dependencies and injects a PNG pixel
decoder, and executes the actual furniture-data loader/constructor with configuration
and localization adapters. It records the renderer commit and source hashes; it does
not edit renderer source or claim GPU/browser rendering.

The preview owner must review and mount this private overlay read-only with a union
of base and new Nitro/icon files, and route the furniture-data URL to its merged JSON.
Keep `%libname%` as the actual original classname in `furni.asset.url` and
`furni.asset.icon.url`; the usual icon template is `/icons/%libname%%param%_icon.png`.
No client-global replacement or asset publication is performed by these scripts.
