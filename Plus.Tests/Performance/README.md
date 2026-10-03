# Room performance reproduction

This is an in-memory microbenchmark of the real RoomUserManager, Room.SendPacket,
RoomUser.Chat, UserUpdateComposer, Flash packet factory and GameClient.Send paths.
It does not start a hotel, use a database or open sockets. The callback accepts
encoded bytes in memory. All workloads retain 500 entities; bot cases add one
observer where indicated. The bot movement fixture has no AI timer and uses
precomputed paths, so it measures manager movement rather than pathfinding or
GenericBot target selection.

## Run

```bash
dotnet test Plus.Tests/Plus.Tests.csproj -c Release -p:Platform=AnyCPU
DOTNET_TieredCompilation=0 PLUSEMU_ROOM_BENCHMARK=/tmp/room-performance-fixed.txt \
  dotnet test Plus.Tests/Plus.Tests.csproj -c Release -p:Platform=AnyCPU --no-build \
  --filter FullyQualifiedName~RoomPerformanceBenchmarks
cat /tmp/room-performance-fixed.txt
```

The benchmark runs only when PLUSEMU_ROOM_BENCHMARK names an output file. It warms
up each workload, collects garbage before measurement and reports elapsed time
and GC.GetAllocatedBytesForCurrentThread per operation. Tiered compilation is
disabled for the comparison to avoid promotion during short workloads. The two
cycle case alternates all 500 bots between adjacent squares, commits movement,
preserves their real indexed occupancy and verifies every bot moved each time.
It includes fixture path setup and a movement check. Timing on this shared VPS
varies; allocation totals are the more stable result.

The baseline is commit 220e5ec0. To reproduce it with exactly the same harness:

```bash
git worktree add --detach /tmp/plusemu-room-performance-baseline 220e5ec0
mkdir -p /tmp/plusemu-room-performance-baseline/Plus.Tests/Performance
cp Plus.Tests/Performance/RoomPerformanceBenchmarks.cs \
  /tmp/plusemu-room-performance-baseline/Plus.Tests/Performance/
dotnet build /tmp/plusemu-room-performance-baseline/Plus.Tests/Plus.Tests.csproj \
  -c Release -p:Platform=AnyCPU
DOTNET_TieredCompilation=0 PLUSEMU_ROOM_BENCHMARK=/tmp/room-performance-baseline.txt \
  dotnet test /tmp/plusemu-room-performance-baseline/Plus.Tests/Plus.Tests.csproj \
  -c Release -p:Platform=AnyCPU --no-build \
  --filter FullyQualifiedName~RoomPerformanceBenchmarks
```

Run the two benchmarks sequentially without concurrent builds. The solution maps
the emulator to x86, which can overwrite test dependencies with an assembly the
Linux test host cannot load. Rebuild the test project with Platform=AnyCPU after
building the solution. The solution's example plugin also needs the existing
PLUS_EMULATOR_HOME setting to find the emulator assembly:

```bash
PLUS_EMULATOR_HOME="$PWD/bin/Release/net10.0" \
  dotnet build 'Plus Emulator.sln' -c Release
```

## Recorded comparison

.NET 10.0.12, Release, AnyCPU, tiered compilation disabled, 20 warmups per workload.
Times are milliseconds per operation; allocations are bytes per operation.

| Workload | Baseline ms | Fixed ms | Baseline bytes | Fixed bytes |
| --- | ---: | ---: | ---: | ---: |
| GetUserList, 500 bots + observer | 0.0073 | 0.0067 | 4,056 | 4,056 |
| GetRoomUsers, 500 bots + observer | 0.0120 | 0.0068 | 4,208 | 152 |
| 500 empty-square lookups | 0.0156 | 0.0240 | 16,000 | 16,000 |
| 500 occupied-square lookups | 0.0207 | 0.0262 | 0 | 0 |
| CycleUsers, 500 idle bots | 0.0254 | 0.0219 | 8,168 | 4,144 |
| CycleUsers, 500 precomputed walking plans | 0.3288 | 0.1619 | 76,168 | 56,144 |
| Two cycles, 500 committed walking steps | 3.5912 | 3.5820 | 348,424 | 324,376 |
| 500 bot chat messages to one observer | 7.1979 | 4.6007 | 4,448,103 | 616,000 |
| 500 avatar status updates to one observer | 1.0554 | 0.7341 | 189,388 | 195,664 |
| 500 changed-avatar collection, no recipients | 0.3739 | 0.0243 | 24,616 | 12,760 |
| One 500-avatar GameClient.Send | 0.8008 | 1.1531 | 164,764 | 182,656 |
| 500 small GameClient.Send calls | 1.6561 | 2.7925 | 372,008 | 332,000 |
| 500-avatar packet to 500 recipients | 425.7593 | 0.9746 | 82,378,161 | 307,256 |
| 500 avatar status updates to 500 recipients | 428.0151 | 0.9581 | 82,394,649 | 319,720 |

An earlier run with default tiered compilation measured the 500-recipient packet
at 139.6313 -> 0.6779 ms and full status collection/broadcast at
167.3548 -> 0.5452 ms, with essentially the same allocation totals. These are
microbenchmark observations, not a guarantee of live room tick duration.

## Evidence and behavior

- ConcurrentDictionary.Values already returns a snapshot. Owned cycle/status
  callers copied it again. GetRoomUsers now enumerates the concurrent dictionary
  safely and builds only the human recipient list; GetUserList's public snapshot
  contract remains unchanged.
- SerializeStatusUpdates called List.Contains for every changed entity, producing
  a growing quadratic scan. The dictionary supplies each avatar once; clearing
  UpdateNeeded still suppresses repeat updates.
- Each recipient previously recomposed the entire 500-avatar update, including
  user/status snapshots, strings and StringBuilders. The broadcast now encodes
  once per revision, packet factory, client type and mapped outgoing header, with
  no cache across broadcasts. Fanout remains 500 actual sends.
- Direct and broadcast sends use owned byte arrays. The old direct send returned
  its recyclable stream to the pool before TCP completion, and a regression test
  proves the pending bytes were overwritten on the baseline. The owned copy adds
  roughly 18 KB to a standalone 500-avatar send. SocketAsyncEventArgs is disposed
  on synchronous completion or its async Completed event. The WebSocket adapter
  reports no pending event-args operation after copying into its queue.
- RoomUser.GetClient already caches the real client and returns immediately for
  bots; no registry/cache redesign was needed. Square lookup already uses the
  occupancy index; the empty-square list allocation is unchanged and owned by
  the separate map worker.
- Disabled Debug logging no longer formats a string per recipient. The benchmark
  has no configured logging targets. Enabled Debug/Trace console/file I/O is not
  measured; the checked-in config enables Trace, so that can still add cost. One
  event-args object and one transport send per recipient remain necessary here.
  WsSessionProxy still copies the encoded bytes for each recipient; that copy
  and the WebSocket library queue/framing cost are outside these timings.
- Temporary stress bots no longer restore/capture/zero walkability bytes while
  walking or in fallback removal. UpdateUserMovement still maintains occupancy.
  Ordinary bots/users keep their existing reservations. The roaming worker owns
  structural floor validation; this branch does not edit GameMap, GenericBot or
  PathFinding. A normal avatar can still reserve a tile occupied by a stress bot;
  this change specifically removes temporary-bot writes.

RoomBroadcastTests checks 500-recipient wire bytes, revision headers, factory and
client-header isolation, changed-state rebroadcasts, buffer stability after pool
reuse, synchronous/WebSocket/TCP completion behavior, bot/pet preferences and
bubbles, rights-only filtering, status flags, 500 overlapping committed steps,
and ordinary-bot/fallback-removal reservation controls. Three tests fail through
the original baseline call paths: 500 compositions instead of two revision
variants, overwritten pending bytes, and a floor byte changing from 1 to 3.
All 70 Release tests pass on the fixed branch (the benchmark is conditional).

Standards and Spec were self-reviewed separately against 220e5ec0, including new
files, as the task prohibits agents. No unresolved findings. No live game,
full-room ProcessRoom, network bandwidth/backpressure, enabled logging, furniture
or game tick, bot AI/pathfinding workload, or frontend performance is proven by
these results. The 500-bot/one-observer server paths were already small; the
fanout defect is much larger with 500 human recipients. Manual game testing and
parent integration review remain required before publication/deployment.

## Pathfinding v2 P1

```bash
DOTNET_TieredCompilation=0 PLUSEMU_PATHFINDING_BENCHMARK=/tmp/pathfinding.txt \
  dotnet test Plus.Tests/Plus.Tests.csproj -c Release -p:Platform=AnyCPU --no-build \
  --filter FullyQualifiedName~PathfindingBenchmarks
cat /tmp/pathfinding.txt
```

The environment gate leaves the normal suite fast. The harness compares legacy
and production v2 Find on identical open, maze, directed-cliff unreachable and
separate-component terrain. It verifies v2 against BFS before timing, warms
connectivity/workspace/route buffers, and reports p50/p95/p99, allocations,
expansions, validator calls, heap operations and retained array payloads.
The 64² maze has six alternating wall openings, a 133-step route and over 2,000
expansions; the 256² cliff proves unreachable only after visiting the left half.
Targets from spec §8 are printed alongside observations, not asserted as portable
latency guarantees on the shared VPS. No timing assertions mask correctness.

The movement row uses 100 temporary bots on 64² and two real legacy movement
cycles plus fixture setup. It reports the existing executor baseline: P1 does
not implement the P2 movement phases or their zero-allocation target. Hook/status
allocation remains included in that row. Workspace retention is globally bounded
to 64 MiB and per size to the room-worker concurrency; active-node count changes
cannot retain unbounded room-sized arrays.

Legacy public method signatures and behavior stay intact. An internal benchmark
overload collects counters with the same search, including its known heuristic
and mutable-heap defects. V2 changes corner rules and optimizes tick count, so
legacy route length is not used as a correctness oracle.
