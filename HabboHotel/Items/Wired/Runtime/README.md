# Contextual room execution

The room facade dispatches typed events and adapts existing legacy hooks. Each matching
trigger is visited once; each accepted firing owns its selections, long scalar values,
immutable configuration snapshots and variable execution frame. Child calls/signals share
one dispatch identity snapshot and resolve selected targets against current room objects.
Normal actor-bearing delayed firings are cancelled when that exact RoomUser visit leaves;
Leave events explicitly allow room effects after departure, with avatar targets still
requiring attachment. Virtual-ID reuse never grants a new visitor access to old work.

Selectors run first, preserving separate furniture/avatar modification markers. An empty
selector result stays empty. Filtering replaces only its target kind and inversion occurs
once. Quantity policies are gathered before other policies; ordinary source resolution caps
all explicit sources while raw selector/world capture bypasses caps. Unlike Polaris's fresh
random subset on every resolution, each firing caches a subset per source/saved picks/name/
limit and checks identity on every use. Multiple positive limits choose the smallest value.

Policies precede conditions; scoped condition groups retain ordinary requirements outside
the group. Legacy repeaters preserve each-condition-ANY-avatar semantics even on mixed
configured stacks, then run room actions once without an actor. Quota acquisition and
stateful action selection happen after the relevant condition branch is selected. Trigger
firings select positive actions when conditions pass and negative actions when they fail.
Explicit positive/negative calls gate passed/failed target conditions respectively and then
execute positive target actions. The same rule applies to addressed negative signals.

All action bodies and auxiliary animation callbacks use the foundation's individual-action
priority queue and per-pass budget. Normal delays remain independent half-second units;
ordered execution is opt-in. False results continue, unless an explicit stop-on-success
policy requests otherwise. Queued events, signal envelopes and action chains share one pending limit;
recursive calls/signals carry depth. Signal payloads copy selections and scalar values.
Queued events reserve one pending slot for each matched stack before acceptance.
Predicates and accepted evaluations resume from their next stage without replaying selectors,
stateful policies or conditions. Matching triggers/configured stacks retain object identity;
editing or moving a captured box cancels its unfinished work. A completing evaluation transfers
its reserved slot into the action chain. Synchronous speech/command gates fail closed when
the configured pass budget cannot finish their decision; chat is never consumed later.
Accepted due work drains before fresh timer polling; timer polling rotates across boxes
when the per-pass budget cannot visit them all. Signal triggers resume across passes.
Auxiliary cancellation restores transient state exactly once without another timer.

PublishConfigured, PublishLegacy and PublishPromotion persist detached validated state
under engine ownership before changing the live registration. Persistence failures leave
live state and pending work intact. Successful publication cancels captured work before
new configuration takes effect. Promotion keeps the legacy wired_items payload available.
Direct ApplyConfiguration is a nonthrowing assignment for already validated input; it must
not recursively call a publisher. Concrete loaders must preserve legacy behavior without
a sidecar and surface invalid sidecars without replacing or overwriting their bytes.
Fresh variable boxes retain unconfigured editor drafts without ApplyConfiguration or
persistence authority. Their first validated save expects no existing sidecar.

Rooms requiring modern timers, pending contextual actions or signals join a fast-room
registry. The existing game loop starts wired-only passes at 50ms using the same ProcessTask
as the 500ms full room pass. A busy room receives no concurrent task or catch-up burst.
Fast misses do not increment legacy lag, walking, game, idle or furniture counters; the
legacy crash threshold remains 30 half-second misses. Rooms without fast work perform no
extra room scan at the fast cadence.

Runtime and room-cadence tests cross the actual engine/scheduling seam with controlled
clocks and fake configured boxes. They verify dispatch/selection ordering, both condition
branches, forwarded sources, cached caps, synchronous chat, immutable delayed settings,
publication failures/cancellation, visit identity, mixed repeaters, shared limits, recursive
calls, copied signals, auxiliary work and serialized cadence. These tests do not establish
full Turbo catalogue parity, production load capacity or live client animation fidelity.
