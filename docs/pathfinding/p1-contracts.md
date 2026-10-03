# Pathfinding v2 P1 contracts

Contract: PATHFINDING-V2-SPEC.md r11 (2026-10-03), phase P1. Legacy remains the
movement executor. Each room snapshots `pathfinding.*` settings through
SettingsManager at load. Missing values remain distinct from zero. `engine=shadow`
runs the production v2 search at the legacy recalc point, reads GoalX/GoalY and
logs sampled route/outcome differences, both timings and expansions. It never
installs the result. `engine=v2` and layering settings log a P1 warning and leave
movement on legacy. Rooms larger than 256 on either model axis have no v2 adapter.

Furniture records are published even in legacy mode, so all placements, moves,
rotations, state and adjustable-height changes, rollers, individual and bulk
pickups are covered. Item setters serialize on NavSync; multi-field placement
and SetState transactions suppress intermediate publication. Removed instances
are detached and leave a versioned tombstone. Only the room thread applies the
grid. The compiler reads each live record once at the apply boundary and computes
old/new footprint closure before compiling and committing AppliedRecords.

Compatibility mode has only tile slots, including holes for blocked/void tiles:
SlotCapacity = width × height while ActiveNodeCount counts standable surfaces.
Overflow slots/free lists, contact ownership, lifecycle owner scopes, move-command
CAS slots, claims and the executor are P2/P3 work, not P1 substitutes.

## Legacy assertion classification

The rows cover every assertion in the named legacy test. “Retained utility” means
the existing assertion still exercises the shared room/roaming utility; P1 does
not replace target selection or admission. “Retained search” additionally runs
through production v2 Find in the profile/BFS tests and the retained corpus.
“Replaced edge” means the legacy validator assertion remains as a legacy contract,
while its v2 replacement requires adjacent edges and the official corner rule.
No legacy assertion is removed or relaxed.

| BotRoamingTests method | Assertion classification and v2 evidence |
| --- | --- |
| IncludesBorderAndDoorLineTilesAndNeverFallsBackToABlockedCorner | Retained utility: all target-domain, random coverage, door exclusion and no fallback assertions. |
| KeepsOpenTilesOnTheDoorRowAndDoorColumn | Retained utility: row/column coordinates and blocked corner exclusion. |
| ReturnsNoTargetWhenNothingIsWalkable | Retained utility: empty domain, false Try and legacy fallback point. V2 has InvalidGoal for an absent surface. |
| CrowdZeroingLiveTilesDoesNotShrinkOrRebuildTargets | Retained utility: cached reference, coordinate coverage, stress/live distinction. Retained search: stress IgnoreUsers in BFS and retained corpus. |
| TerrainStatusReachesStressTargetsAndLiveOccupancyDoesNot | Retained utility: live occupancy and structural cache. Retained search: FloorLocked and conflict matrix tests. |
| GateCloseAndOpenUpdateTheCacheAndTemporarySteps | Retained utility: cache and live bytes. Retained search: structural lock/open surface, stress profile, LegacyOverride. |
| OrdinaryGenericBotDoesNotTargetAnOccupiedTile | Retained utility: every AI goal/domain assertion. Retained search: occupied goal policy tests. |
| FloorStatusChangesInvalidateTheCachedTargets | Retained utility: reference changes, statuses 0/2/3 excluded from random targets. Retained search: compiler goal-only surfaces and locks. |
| GenerateMapsRebuildsOpenFloorAndOccupancyWritesDoNot | Retained utility: regeneration and cached reference. V2 base terrain is immutable; furniture/lock rebuilds are record based. |
| GenerateMapsLeavesTemporaryTilesOpenAndReservesOrdinaryUsers | Retained utility: occupancy byte and SqState assertions (P2 maintains these). Replaced edge: non-adjacent IsValidStep2 acceptance becomes a route of legal adjacent steps. |
| OnlyTemporaryBotsLoseWallOverride | Retained search: staff LegacyTile crosses void; temporary IgnoreUsers cannot. Retained corpus and BFS. |
| StressBotPathsAroundWallsAndCliffsWhileStaffOverrideMayCrossThem | Retained search: wall avoidance, occupied transit and staff override. Retained corpus. Optimal routes and official corners replace exact legacy shape. |
| TemporaryOverrideRejectsWallsAndHeightAndAcceptsOccupiedFloor | Retained search: wall/height/occupancy capabilities. Replaced edge: non-adjacent validator success is replaced by adjacent route validation. |
| StressBotDoesNotClimbAStepTallerThanTheWalkLimit | Retained search: 2.0 cliff remains unreachable under plus. Retained corpus. |
| GenericBotRoamsARealTileAndStaysPutWhenTheFloorIsBlocked | Retained utility: goal selection, no fallback intent and PathRecalcNeeded unchanged. Real shadow recalc test verifies movement flags and goals. |
| PickingFiveHundredTargetsOnALargeFloorStaysCached | Retained utility: border bounds, domain size/reference and cached-pick timing. Search is separately benchmarked. |

| StressBotTests method | Assertion classification and v2 evidence |
| --- | --- |
| ValidatesEntityAndCount | Retained utility: all argument/count cases. |
| RejectsMissingAndExtraArguments | Retained utility: unchanged command parser. |
| RequiresModeratorRightAndEstablishedStaffPermission | Retained utility: unchanged permissions. |
| ExistingCommandRegistrationDiscoversStressCommand | Retained utility: unchanged registry/header dispatch. |
| CreatesFiveHundredTemporaryGenericBotsThenCapsAndClearsOnlyThem | Retained utility: limits, ownership, ids, temporary/non-temporary admission/removal; P2 will own the navigation lifecycle. |
| ConcurrentRequestsHaveABoundedBacklog | Retained utility: command backlog and counts. |
| TemporaryBotsCanRequestAndFindAPathThroughCrowdedTiles | Retained search: IgnoreUsers, real legacy input recalc, retained corpus and occupied-goal policy. |
| ConcurrentNormalAndStressDeploymentsKeepVirtualLookupAndClearConsistent | Retained utility: concurrent ids, virtual lookup, clearing only temporary actors. P2 admission/removal tests remain deferred. |

Production Find is checked against an independent FIFO BFS without its connectivity
or HasWayIn prechecks on 10,000 seeded grids × six profiles (ordinary, staff,
stress, rider, walkthrough and cardinal-only), under both height profiles and all
corner rules. Every returned edge is validated; goldens cover all octants, U,
symmetry and stairs. Forced thread schedules test CAS, dirty drains, transaction
selection and multi-field publication, followed by concurrent mutation stress.

P1 test-plan entries for older MoveCommand CAS races and the room-specific
thread-local lifecycle owner scope belong to P2 and are intentionally absent.
Overflow free-list holes and layered contact tests belong to P3; P1 verifies high
sparse tile slots, overlapping zero-height walkable items, shuffled records,
sub-0.001 heights and actor-aware occupied/shared-door goals.

The Walk Magic compiler hook is explicitly marked TODO because this base lacks
InteractionType.WalkMagicTile. The parallel magic PR supplies that interaction;
this PR does not invent it or implement its executor/widget behavior.

The height parity corpus excludes ambiguous legacy stack ordering, adjustable-seat
Z (D9 fix), actor-dependent guild access and raised open gates. Those cases have
explicit compiler/rule tests. Furniture on model void becomes standable only
when an effective furniture surface exists; the underlying terrain copy never
reads legacy OpenSquare. Closed/blocked tiles remain blocked in v2 even if the
legacy executor's D7 side effect accepts them.

Live hotel shadow divergence capture and Nitro manual movement validation require
staging/live access and are rollout gates, not claims made by this local suite.
