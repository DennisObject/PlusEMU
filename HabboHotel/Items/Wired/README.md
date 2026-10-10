# Room wired engine foundation

`WiredComponent` retains the existing room/caller hooks and box factory. `WiredStackEngine`
owns trigger dispatch, tile stacks, condition evaluation, random effect selection and delayed
action chains. Each firing carries a copied legacy argument array and a stack-call depth.
This is the legacy foundation for later contextual selectors/signals/variables, not a port
of Turbo's complete wired catalogue.

## Compatibility and deliberate changes

- All 50 previously constructible boxes remain available, including Plus command/bot/team/
  badge/map/roller effects and the existing no-op reward box. Unsupported enum values are
  logged and skipped during room loading. The negative badge/FX boxes now report their own
  enum type instead of the corresponding positive type.
- `IWiredItem`, `IWiredCycle`, `wired_items` columns, selected-item/snapshot strings and save
  parsers retain their existing shapes. Only periodic triggers use `IWiredCycle.OnCycle`;
  effect `Execute` methods are immediate bodies invoked by the room scheduler.
- Conditions and the random addon are evaluated in one stack pipeline. Random selects one
  actual effect, excluding the addon, and retains the actor. Every registered trigger is
  visited once per dispatch, and results are ORed. A failing trigger does not hide another
  successful trigger.
- Speech matching and command matching remain in their existing boxes. Chat consumption is
  decided synchronously once conditions pass and the stack is accepted, even when an effect
  is delayed or fails. The original self-whisper is sent at acceptance.
- Configured action delays are nonnegative units of 500ms, measured independently from
  firing. Equal deadlines execute in height/item-ID order, with stable ordering across
  firings. A zero-delay action executes immediately even when another action waits; two
  delay-1 actions both become due after 500ms. A late room tick executes due actions within
  the execution budget. This preserves Plus/Volt independent effect timing instead of
  adopting Turbo's cumulative chain delays, which would change existing rooms. Legacy
  shared flags and inconsistent tick offsets no longer collapse or postpone firings.
- Each firing retains its own actor and captures the actual RoomUser visit reference.
  Leaving, changing rooms or re-entering cancels the old visit's remaining actions. Actorless
  repeater effects still execute once. The repeater intentionally preserves its legacy quantifier:
  **each condition must match some room actor**, and different conditions may match
  different actors. A failed condition now resets the repeater timer instead of polling
  every cycle. Game start/end conditions, previously ignored, now gate effects per actor.
- An action returning false or throwing is logged when applicable and does not stop later
  actions. Stack calls deduplicate selected tiles and use the same conditions/random/
  scheduling pipeline with a depth guard, including across delayed calls.
- The kick effect retains its intrinsic 1500ms grace independently of its stored delay.
  Its eligibility and warning are prepared synchronously when the firing is accepted;
  removal happens after the grace period. Teleport's glow likewise begins at firing.
  These legacy preparation hooks run once per selected action and actor. Match-position applies
  each selected item's saved snapshot once, checks attachment correctly and accepts the five-field state tuple.
- Pickup/removal cancels captured pending stacks, including removal followed by re-addition.
  Moving the source cancels its old chain even if it returns before the next engine pass;
  Item.SetState tracks movement generations. Moving an action away skips that action. The
  tile index is rebuilt at engine seams to observe mutable X/Y/Z without new owner hooks.
  Cleanup drops pending work. A successful `SaveBox` cancels chains containing any box in
  the captured stack. Legacy `HandleSave` still mutates outside the engine lock; atomic
  parse/validate/apply integration remains the protocol owner's follow-up.

## Limits and acceptance evidence

Optional existing `server_settings` keys are read when the room engine is created:
`wired.max_depth` (default 32), `wired.max_executions_per_pass` (default 10000), and
`wired.max_pending_stacks` (default 10000). Missing/nonpositive values use those defaults.
The execution budget counts individual trigger, condition, firing preparation and action calls.
Actions that exhaust the budget resume on later room cycles. Individual actions share a global
queue ordered by due time, captured height, item ID and firing sequence, including late ticks.
The pending limit includes active chains so nested calls cannot exceed it before their caller
is queued.

`Plus.Tests/WiredStackEngineTests.cs` exercises the real engine with fake boxes, a controlled
clock, and the production actor-room check. It covers condition gating, random actor
retention, trigger result aggregation, synchronous chat acceptance, independent/stable delay
ordering, late ticks, repeated and concurrent firings, room changes, detachment, movement,
save cancellation, cleanup, failure continuation, repeater quantifiers, pipeline stack calls,
delayed recursion and per-action/pending budgets. Factory coverage checks the complete
50-box inventory and retained configuration properties. Synthetic room/client fixtures also
check the real kick warning/removal packet order, protected kick actors and firing-time
teleport glow. These checks do not prove network-visible
furniture movement or live gameplay; no running room/database was used for these checks.
