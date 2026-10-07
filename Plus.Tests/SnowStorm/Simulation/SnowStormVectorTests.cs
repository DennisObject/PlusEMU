using System.Text.Json;
using System.Text.Json.Serialization;
using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;
using static Plus.Tests.SnowStorm.Simulation.SimulationTestSupport;

namespace Plus.Tests.SnowStorm.Simulation;

// Golden vectors for cross-language parity (C# server sim vs the TS client sim). Each scenario drives the arena the
// way the server does (RunTurn, then refills/pickups, then validated inputs for the next turn) and records what clients
// receive. Set SNOWSTORM_VECTOR_DIR to write <name>.json there; otherwise the test only checks determinism.
//
// JSON shape (camelCase):
// {
//   "format": "snowstorm-sim-vector/1", "name": string, "description": string, "numberOfTeams": int,
//   "level": { "width", "height", "heightMap" (rows joined by "\r"),
//              "fuseObjects": [{ "name", "id", "x", "y", "xDimension", "yDimension", "height", "direction", "altitude",
//                                "canStandOn", "stuff" }] },
//   "initialObjects": [{ "variables": int[] (type, id, ...), "strings"?: [name, mission, figure, sex] }],  // StageStarting
//   "turnCount": int,
//   "events": [{ "turn", "subturn", "type", "fields": int[] }],  // apply order; turn T = the events of GameStatus(T - 1)
//   "checksums": int[turnCount],                                // checksums[T] = checksum after simulating turn T
//   "dumps": [{ "turn", "checksum", "objects": [same as initialObjects] }],  // state after turn 49, 99, ... and the last
//   "final": { "teamScores": int[], "players": [{ "humanId", "userId", "team", "score", "kills", "deaths",
//              "snowballHits", "snowballHitsTaken", "snowballsThrown", "snowballsCreated", "snowballsFromMachine",
//              "friendlyHits", "friendlyKills" }] }
// }
// Replay: build the stage from "level", add "initialObjects" in order, queue every event at (turn, subturn), then per
// turn apply the subturn's events before the objects move; compare checksums[T] and the dumps.
public class SnowStormVectorTests
{
    private const int DumpInterval = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static TheoryData<string> Scenarios => ["arctic_island_2v2", "knockdown_chain", "lob_over_trees"];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ScenarioIsDeterministicAndExercisesItsRules(string name)
    {
        var first = Run(name);
        var second = Run(name);
        string json = JsonSerializer.Serialize(first.Vector, JsonOptions);

        Assert.Equal(json, JsonSerializer.Serialize(second.Vector, JsonOptions));
        AssertScenario(name, first);

        if (Environment.GetEnvironmentVariable("SNOWSTORM_VECTOR_DIR") is { Length: > 0 } directory) {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".json"), json + "\n");
        }
    }

    private static void AssertScenario(string name, ScenarioRun run)
    {
        var stats = run.Arena.Humans.Select(human => run.Arena.GetStats(human.Id)).ToList();
        var events = run.Vector.Events;
        Assert.Contains(events, scheduled => scheduled.Type == 8);

        switch (name) {
            case "arctic_island_2v2":
                Assert.Equal(400, run.Vector.TurnCount);
                Assert.Contains(events, scheduled => scheduled.Type == 2);
                Assert.Contains(events, scheduled => scheduled.Type == 7);
                Assert.Contains(events, scheduled => scheduled.Type == 11);
                Assert.Contains(events, scheduled => scheduled.Type == 12);
                Assert.True(stats.Sum(stat => stat.SnowballHits) > 0);
                Assert.True(stats.Sum(stat => stat.SnowballsFromMachine) > 0);
                break;
            case "knockdown_chain":
                Assert.True(stats.Sum(stat => stat.Kills) >= 2);
                break;
            case "lob_over_trees":
                Assert.Contains(run.Arena.Objects.OfType<SnowStormTree>(), tree => tree.Hits == tree.MaxHits);
                Assert.True(stats.Sum(stat => stat.SnowballHits) > 0);
                break;
        }
    }

    private static ScenarioRun Run(string name) => name switch
    {
        "arctic_island_2v2" => ArcticIsland2v2(),
        "knockdown_chain" => KnockdownChain(),
        "lob_over_trees" => LobOverTrees(),
        _ => throw new ArgumentException(name)
    };

    // Two north and two south players on the official arena: they walk onto the central island, throw default
    // throws at the nearest opponent and make snowballs when empty; one north player camps the machine pickup tile.
    private static ScenarioRun ArcticIsland2v2()
    {
        var arena = SnowStormArena.Create(ArcticIslandFixture.Level(), 2);
        var rules = new SnowStormServerRules(arena);
        var spawns = rules.ChooseSpawns([1, 2, 1, 2],
            new Dictionary<int, IReadOnlyList<(int X, int Y)>> { [1] = ArcticIslandFixture.NorthSpawns, [2] = ArcticIslandFixture.SouthSpawns },
            new Random(2026));
        var humans = spawns.Select((spawn, index) =>
            arena.AddHuman(Player(index + 1, index % 2 + 1), spawn.X, spawn.Y, spawn.BodyDirection)).ToList();
        (int X, int Y)[] goals = [(26, 25), (27, 27), (24, 22), (27, 31)];

        return Simulate("arctic_island_2v2", "Official Arctic Island 2v2: walking, default throws, making, machine refills and pickups.",
            arena, rules, 400, turn =>
            {
                for (var index = 0; index < humans.Count; index++) {
                    var human = humans[index];

                    if (turn == 2 + index) {
                        rules.TryScheduleMove(turn, index % 3, human.Id, World(goals[index].X), World(goals[index].Y));
                    }

                    if (turn < 40 || (turn + index * 3) % 9 != 0) {
                        continue;
                    }

                    if (human.SnowballCount == 0) {
                        rules.TryScheduleMakeSnowball(turn, 1, human.Id);
                        continue;
                    }

                    var target = humans.Where(other => other.Team != human.Team && arena.GetObject(other.Id) != null)
                        .OrderBy(other => Math.Abs(other.X - human.X) + Math.Abs(other.Y - human.Y)).First();
                    rules.TryScheduleThrowAtHuman(turn, 2, human.Id, target.Id, SnowStormSnowball.TrajectoryDefaultThrow);
                }

                if (turn == 300) {
                    rules.TryScheduleMove(turn, 0, humans[3].Id, World(30), World(40));
                }
            });
    }

    // Two throwers per side on open ground: the red target is knocked down repeatedly while balls pass through it
    // during stun and invincibility; empty throwers make new snowballs.
    private static ScenarioRun KnockdownChain()
    {
        var arena = SnowStormArena.Create(OpenLevel(24, 12), 2);
        var rules = new SnowStormServerRules(arena);
        var blueA = arena.AddHuman(Player(1, 1), 3, 5, 2);
        var blueB = arena.AddHuman(Player(2, 1), 3, 8, 2);
        var redA = arena.AddHuman(Player(3, 2), 9, 5, 6);
        var redB = arena.AddHuman(Player(4, 2), 9, 8, 6);
        SnowStormHuman[] blues = [blueA, blueB];

        return Simulate("knockdown_chain", "Open 24x12 field: repeated knockdowns, stun and invincibility pass-through, making snowballs.",
            arena, rules, 300, turn =>
            {
                foreach (var blue in blues) {
                    if (blue.SnowballCount == 0) {
                        rules.TryScheduleMakeSnowball(turn, 0, blue.Id);
                    }
                    else if (turn % 2 == (blue == blueA ? 0 : 1)) {
                        rules.TryScheduleThrowAtHuman(turn, 1, blue.Id, redA.Id, SnowStormSnowball.TrajectoryQuickThrow);
                    }
                }

                if (turn % 7 == 0 && redB.SnowballCount > 0) {
                    rules.TryScheduleThrowAtHuman(turn, 2, redB.Id, blueB.Id, SnowStormSnowball.TrajectoryShortLob);
                }
                else if (redB.SnowballCount == 0) {
                    rules.TryScheduleMakeSnowball(turn, 2, redB.Id);
                }

                if (turn == 150) {
                    rules.TryScheduleMove(turn, 0, redA.Id, World(12), World(3));
                }
            });
    }

    // A tree line between the teams: long and short lobs fly over it, low quick balls wear a far tree down.
    private static ScenarioRun LobOverTrees()
    {
        var trees = Enumerable.Range(2, 8).Select(y => Tree(y - 1, 8, y)).Append(Tree(20, 16, 2)).ToArray();
        var arena = SnowStormArena.Create(OpenLevel(30, 12, trees), 2);
        var rules = new SnowStormServerRules(arena);
        var lobber = arena.AddHuman(Player(1, 1), 3, 5, 2);
        var quick = arena.AddHuman(Player(2, 1), 3, 2, 2);
        var targetA = arena.AddHuman(Player(3, 2), 14, 5, 6);
        var targetB = arena.AddHuman(Player(4, 2), 14, 7, 6);

        return Simulate("lob_over_trees", "Tree wall at x=8: lobs over it, a far tree destroyed by low quick throws, quick replies through.",
            arena, rules, 200, turn =>
            {
                if (turn % 4 == 0) {
                    int trajectory = turn % 8 == 0 ? SnowStormSnowball.TrajectoryLongLob : SnowStormSnowball.TrajectoryShortLob;

                    if (!rules.TryScheduleThrowAtHuman(turn, 0, lobber.Id, turn % 16 < 8 ? targetA.Id : targetB.Id, trajectory)) {
                        rules.TryScheduleMakeSnowball(turn, 0, lobber.Id);
                    }
                }

                if (turn % 6 == 1 && !rules.TryScheduleThrowAtPosition(turn, 1, quick.Id, World(29), World(2), SnowStormSnowball.TrajectoryQuickThrow)) {
                    rules.TryScheduleMakeSnowball(turn, 1, quick.Id);
                }

                if (turn % 10 == 5 && !rules.TryScheduleThrowAtHuman(turn, 2, targetB.Id, lobber.Id, SnowStormSnowball.TrajectoryQuickThrow)) {
                    rules.TryScheduleMakeSnowball(turn, 2, targetB.Id);
                }
            });
    }

    private static ScenarioRun Simulate(string name, string description, SnowStormArena arena, SnowStormServerRules rules, int turnCount,
        Action<int> inputs)
    {
        var level = arena.Level;
        var initialObjects = arena.Snapshot().Select(ObjectJson.From).ToList();
        var events = new List<EventJson>();
        var checksums = new List<int>();
        var dumps = new List<DumpJson>();

        for (var turn = 0; turn < turnCount; turn++) {
            // Turn 0 has no preceding GameStatus to carry events.
            if (turn > 0) {
                inputs(turn);
            }

            events.AddRange(arena.GetScheduledEvents(turn).Select(scheduled =>
                new EventJson(scheduled.Turn, scheduled.Subturn, scheduled.Event.Type, scheduled.Event.Fields)));

            var result = arena.RunTurn();
            Assert.Empty(arena.GetScheduledEvents(turn));
            checksums.Add(result.Checksum);
            rules.ScheduleRefillsAndPickups();

            if ((turn + 1) % DumpInterval == 0 || turn == turnCount - 1) {
                dumps.Add(new DumpJson(turn, result.Checksum, arena.Snapshot().Select(ObjectJson.From).ToList()));
            }
        }

        var players = arena.Humans.Select(human =>
        {
            var stats = arena.GetStats(human.Id);

            return new PlayerJson(human.Id, human.UserId, human.Team, human.Score, stats.Kills, stats.Deaths, stats.SnowballHits,
                stats.SnowballHitsTaken, stats.SnowballsThrown, stats.SnowballsCreated, stats.SnowballsFromMachine, stats.FriendlyHits,
                stats.FriendlyKills);
        }).ToList();

        var vector = new VectorJson("snowstorm-sim-vector/1", name, description, arena.NumberOfTeams,
            new LevelJson(level.Width, level.Height, level.HeightMap, level.FuseObjects), initialObjects, turnCount, events, checksums,
            dumps, new FinalJson(arena.TeamScores.ToList(), players));

        return new ScenarioRun(arena, vector);
    }

    private sealed record ScenarioRun(SnowStormArena Arena, VectorJson Vector);

    private sealed record VectorJson(string Format, string Name, string Description, int NumberOfTeams, LevelJson Level,
        List<ObjectJson> InitialObjects, int TurnCount, List<EventJson> Events, List<int> Checksums, List<DumpJson> Dumps, FinalJson Final);

    private sealed record LevelJson(int Width, int Height, string HeightMap, IReadOnlyList<SnowStormFuseObject> FuseObjects);

    private sealed record ObjectJson(int[] Variables, string[]? Strings)
    {
        public static ObjectJson From(SnowStormObjectSnapshot snapshot) => new(snapshot.Variables, snapshot.Strings);
    }

    private sealed record EventJson(int Turn, int Subturn, int Type, int[] Fields);

    private sealed record DumpJson(int Turn, int Checksum, List<ObjectJson> Objects);

    private sealed record FinalJson(List<int> TeamScores, List<PlayerJson> Players);

    private sealed record PlayerJson(int HumanId, int UserId, int Team, int Score, int Kills, int Deaths, int SnowballHits,
        int SnowballHitsTaken, int SnowballsThrown, int SnowballsCreated, int SnowballsFromMachine, int FriendlyHits, int FriendlyKills);
}
