# Additive Wired furniture and catalogue import

This is migration 16's explicit data operation. It adds definitions and a separate
`plus_wired_verified_v1` catalogue page, using database-allocated IDs. It never changes
or removes existing furniture, offers, pages, placed items, inventories, settings,
wired configurations, or any of the 50 legacy `wired_id` meanings. New definitions
use `wired_id=0`; the integrated factory resolves their canonical item names.
Executing migration 16's SQL file alone intentionally does nothing.

## Evidence and limits

`manifest.json` covers the immutable registry `58a2ee4767379dc72e037cab4c061ff33066c0f5`
(172 canonical names), plus four clock/game counters and two antennas. The local
`nitro-assets/furniture/json/FurnitureData.json` supplies exact sprite IDs, dimensions,
default rotation and flags. Each matching `.nitro` bundle was inflated using the
local renderer's `NitroBundle` format and checked for its own classname, PNG texture,
logic-model dimensions, height and directions. SHA-256 digests pin the reviewed inputs.

150 canonical names and all six auxiliaries have real matching assets. Heights range
from 0.35 to 1.2, rather than using a blanket box height. All verified directions are
cardinal, so `extra_rot=0`. The furniture schema stores neither default direction nor
an allowed-direction list; those remain in the manifest and the unchanged client asset.
Stacking is enabled for the new boxes; walkability and seating follow FurniData.

22 canonical names have no local FurniData entry or matching original asset:

- Actions: `wf_act_click_conf`, `wf_act_log`, `wf_act_move_furni_as_group`,
  `wf_act_neg_log`, `wf_act_place_furni`, `wf_act_remove_furni`, `wf_act_teleport_to_room`.
- Trigger: `wf_trg_click_user`.
- Variables: `wf_var_echo`, `wf_var_quest`, `wf_var_quest_chain`.
- Addons: `wf_xtra_mov_curve`, `wf_xtra_rotate_to_dir`, `wf_xtra_text_output_furni_name`,
  `wf_xtra_var_fx_boss`, `wf_xtra_var_fx_health`, `wf_xtra_var_fx_level`,
  `wf_xtra_var_fx_number`, `wf_xtra_var_fx_progress`, `wf_xtra_var_fx_status`,
  `wf_xtra_var_lvlup_system`, `wf_xtra_var_time_util`.

The source-backed Polaris aliases `wf_trg_click_bot`, `wf_act_forward_user_to_room`,
`wf_act_tele_room`, `wf_xtra_mov_animation`, and the `wf_xtra_varfx_*` family also have
no trusted local FurniData/asset entries. They cannot establish original visual parity.
Missing assets and factory-unimplemented boxes are excluded by default.

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

## Optional generic appearances for custom boxes

`generic_visual_proposal` explicitly proposes a shared, real category-matched sprite
for each missing custom name: actions use `wf_act_show_message` (3681), triggers use
`wf_trg_enter_room` (3683), variables use `wf_var_room` (15364), and addons use
`wf_xtra_or_eval` (13272). This is a generic fixture
appearance, not an alias claiming original artwork. It is excluded unless the caller
opts in with `--allow-generic-appearance` after reviewing these limitations.

The proposed reuse preserves separate server base-item IDs and canonical interactions.
Source evidence: Plus `ItemDataManager.Items` is keyed by base ID;
`PurchaseFromCatalogEvent` resolves page and offer IDs; room `OpenWiredEvent` resolves
placed item IDs; Octane `WiredView` dispatches by packet envelope and descriptor code.
Existing sprite owners remain unchanged. Generic definitions disable gifting, trading,
marketplace sale and inventory stacking to avoid grouping unrelated server definitions.

The client still sees the donor's thumbnail, hover name, editor title, and asset logic.
Octane `WiredBaseView` reads the title from `getFloorItemData(spriteId)`, and
`useWired.selectObjectForWired` compares local classname/name/furniline for type
selection. Client type grouping cannot distinguish the shared appearances. This
option is for explicit fixture testing; runtime placement/edit/pickup still needs
manual verification before treating the generic boxes as a completed user experience.
It does not edit shared client assets or generate a private FurniData overlay.

## Commands

Run from this worktree/repository root. Python uses only its standard library; Docker
and the already-running isolated preview database are required for online inventory.
No credential is read from a private file or printed: the DB container expands its
own `MARIADB_ROOT_PASSWORD` internally.

```sh
python3 scripts/import-wired-catalog.py \
  --assets /home/ubuntu/dev/plus/nitro-assets/furniture \
  --support-ledger Database/WiredCatalog/factory-support.json \
  --report /tmp/wired-plan.json

python3 scripts/check-wired-catalog.py \
  --assets /home/ubuntu/dev/plus/nitro-assets/furniture \
  --support-ledger Database/WiredCatalog/factory-support.json
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
  --support-ledger Database/WiredCatalog/factory-support.json \
  --expected-engine-commit FULL_REVIEWED_HASH \
  --apply-isolated --report /tmp/wired-applied-plan.json
```

There is no configurable live DB target or deployment step. Existing catalogue pages
and pricing remain unchanged. The new fixture offers cost zero, are ordinary single
items, have no client-global FurniData offer ID, and reside in their own visible page.
Stop the emulator before fixture application and reload definitions/catalogue afterward
within the separately authorized preview workflow. This importer creates no room or
account and performs no service action. Retain the plan with allocated IDs from the
next read-only run if later fixture cleanup is needed; do not delete definitions while
placed/inventory items reference them.
