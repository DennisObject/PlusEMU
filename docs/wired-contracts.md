# Modern Wired configuration contracts

The registry contains all 172 named boxes registered by Turbo at `d5a5474776f92825b1a9fbf090ce1263ef96d546`: 25 triggers, 49 actions, 42 conditions, 20 selectors, 28 addons and eight variables. Four Wired counters are separate room furniture. Each descriptor records the engine category, Turbo category-local code, Octane editor code and the corresponding Polaris configuration source. Descriptors alone do not advertise implemented behavior. A real factory must construct a concrete box and mark its descriptor `Implemented`; unimplemented entries must be skipped visibly.

The client contract is Octane/Polaris's established legacy envelope, not Turbo's generic v2 format. All actions, selectors, addons and variables use the action envelope. Triggers and conditions retain their own envelopes. The client sends:

```
itemId, intCount, ints..., text, selectionCount, selectedIds...,
[actionDelay], selectionCode
```

The server sends:

```
false, selectionLimit, selectionCount, selectedIds..., spriteId, itemId,
text, intCount, ints..., selectionCode, editorCode,
[actionDelay], [blockedCount, blockedSpriteIds...]
```

Conditions omit the blocked list. `OpenWiredEvent` receives only one item id at client header 768. `HideWiredConfigComposer` is the existing empty save-success packet (Octane header 1155); `WiredValidationErrorComposer` sends an error string (156). Revisions translate the existing envelope headers. Private Octane revision overlays need the new incoming `OpenWiredEvent: 768` and outgoing `WiredValidationErrorComposer: 156` mappings. Variable FX packets reserve outgoing mappings `WiredVariableFxConfigsComposer: 9473`, `WiredVariableFxConfigsRemovedComposer: 9474`, `WiredVariableFxStatusComposer: 9475` and `WiredVariableFxStatusRemovedComposer: 9476`; private overlays must also include them.

Turbo's source arrays, variable string arrays, second selections, advanced and wall flags, defaults and contexts do not belong in these envelopes. Octane stores sources in per-box integer or JSON fields. Concrete boxes decode their own editor schema into immutable `WiredConfiguration` source dictionaries, variable tokens, secondary selections and snapshots, while retaining the original integer and text fields for reopening the editor. There are no universal source offsets. `WiredSources` records Octane values (trigger 0, selected 100, selector 200, signal 201, clicked user 11); values overlap between furni, users and bots and must be interpreted by their named slot.

Examples of incompatible layouts: Octane relative move uses five integer fields, including direction and distance separately; Turbo uses signed offsets. Octane give-variable uses target, override, initial value, user source and furni source with a string variable identifier; Turbo transports variable IDs separately. Octane `wf_trg_at_time_long` editor 30 differs from Turbo's inherited long-repeater code 12. Runtime implementations must follow the actual client semantics rather than reuse upstream parameter indexes.

Apply `Database/Migrations/14_AddWiredItemConfigurations.sql` explicitly before enabling configured boxes. The companion table preserves `wired_items` and its existing five-column writer. No old rows are rewritten and no schema change runs automatically. New configuration loads require matching box name and schema version. The factory must also run its concrete validator before applying a loaded configuration. Legacy definitions and their `wired_id` fallback remain available; canonical names may be held in `item_name` when a deployment's old `interaction_type` column is too short.

`IWiredConfiguredItem.TryValidateConfiguration` is pure: it validates its per-box schema and returns a complete immutable candidate. `WiredConfigurationSave` checks bounds and room selections, validates the concrete box, persists one companion row, then calls `ApplyConfiguration`. A failed validation or database write never publishes a candidate. `ApplyConfiguration` must not throw or persist; a room factory supplies its publication seam so applying settings also updates stack metadata and invalidates pending firings under the room engine's ownership. The per-box edit lock does not replace the engine's lock.

Integration requires a real modern factory before the old enum switch, a safe unknown-box path, configuration hydration and the room publication seam. The registry and protocol contracts are prerequisites to runtime coverage, not proof that all modern boxes execute. Context/reference/quest storage, internal variable behavior, projectile timing and movement policies must be verified separately.

Provenance: protocol facts were audited against the local Octane renderer at `1879d30e`, Octane at `2aee1e81`, Turbo at `d5a54747`, and [Polaris](https://github.com/duckietm/Polaris-Emulator) at `34bc0d49511c659fd0054d35d957669ebd6ce9ed`. `ConfigurationReference` is relative to Polaris `Emulator/src/main/java/com/eu/habbo`. Polaris, Octane and Octane-Renderer include GPL-3.0 license files. No checked-in license was found in the audited Turbo or Plus roots. This change independently implements the configuration and protocol contracts; it does not copy either reference runtime.

The legacy follow-up adapter `WiredLegacySave` validates a complete bounded envelope before replaying the original fields on a detached candidate. It seeds existing string, bool, snapshot storage, selected items and cycle delay; unsupported per-handler integer layouts are rejected rather than reinterpreted. Existing five-column storage stays intact. Integrate the handler through `TrySave` with `(original, candidate) => wired.PublishLegacy(original, candidate, () => wired.SaveBox(candidate))`. The handler now uses this engine callback for supported legacy packet layouts; active modern layouts still require the configured promotion path.

Modern saves may supply `publish: wired.PublishConfigured` and `prepare: WiredRoomOperations.PrepareSnapshots`. The publisher receives the persistence action and must check room attachment, persist, cancel captured firings and publish while holding the engine lock. This route does not hold the per-box lock. Preparation reads selected item state before pure validation; it runs only on an edit, never during hydration or reopening. Bounds and room selections are checked again after preparation and normalization.

Definition recognition preserves explicit `wired_effect`, `wired_trigger` and `wired_condition` categories when `wired_id` maps to a constructible legacy box. This keeps an existing `wf_xtra_random` row in legacy effect lookup. Its canonical descriptor remains available to the modern loader; real configured boxes classify by descriptor even when the preserved definition uses a legacy envelope category. Modern-only definitions classify by registry category. Selector, addon and variable furniture share the existing Wired flash reset.


Movement/style/click packets use symbols `WiredMovementsComposer` (internal and active wire 3999), `WiredFurniMoveStyleComposer` (5110), and `WiredClickSettingsComposer` (internal 9477, active wire 2288). The unique click internal ID preserves existing `TradingCompleteComposer` internal 2288. The example profile retains its existing trade wire 2288 and deliberately omits the incompatible click mapping; it is not a usable active Octane click profile. Active/private profiles must map the click symbol to 2288, with their correct trade mapping. The committed 1.6.6 profile has trade wire 2720 and can carry the click mapping without collision.

`IWiredEditorConfigurationProvider` supplies an optional read-only editor projection to `WiredConfiguredConfigComposer`. Stored `Configuration` remains immutable metadata; reopening a variable editor can display current values without overwriting the saved configuration or recapturing snapshots.


Custom Plus boxes retain their legacy stored string/bool/items fields. Badge opens editor119 with `[0,0,34]` and badge text (maximum50 characters); unsupported user sources are rejected because the legacy executor only targets the triggering user. Roller speed opens editor88 with one integer; accepted edits from -1 through10 translate back to its existing numeric string. Regenerate has no dedicated Octane layout: generic value editor123 exposes its existing text with `[0,0,34]`; changed text is rejected because the runtime has no text setting. These mappings replace the mistaken chat-editor7 fallback, whose current chat save has four integers. Badge award execution remains incomplete in the existing legacy executor; this adapter supplies editor/storage compatibility only.

`IWiredConfigurationPersistenceProvider.PersistConfiguration(validated)` replaces the generic companion-row write when implemented by a configured box. It runs inside the engine publisher’s durable action before Apply. Variable definitions use this seam for one authorized database transaction covering both the sidecar and global editor value; rejection or SQL failure prevents active publication. No database lifecycle write belongs after Apply.
