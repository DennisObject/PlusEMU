using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

/// <summary>Reward tracks against the real revision profiles and the claim write path.</summary>
public class RewardTrackLiveTests
{
    private const string Badge = "ACH_RewardTracksCompleted1";

    [Fact]
    public async Task LoginSendsTracksToTheOctaneClient()
    {
        var client = new TestClient(await Profile("OCTANE-3-6-0-FLOOR-20260909"));
        client.SetHabbo(new Habbo { Id = 7 });
        var manager = await Manager(new FakeDatabase(), new BadgeDefinitions());

        manager.SendTracks(client);

        Assert.Equal(new uint[] { 2327 }, client.Sent);
    }

    [Fact]
    public async Task SendTracksReadsTheInjectedClockOnceAtANonUtcOffset()
    {
        var clock = new CountingClock(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(5)));
        var client = new TestClient(await Profile("OCTANE-3-6-0-FLOOR-20260909"));
        client.SetHabbo(new Habbo { Id = 7 });
        var manager = new RewardTrackManager(NullLogger<RewardTrackManager>.Instance, new FakeDatabase(), new BadgeDefinitions(), clock);
        await manager.Start();
        clock.Reads = 0;

        manager.SendTracks(client);

        Assert.Equal(1, clock.Reads);
        Assert.Equal(new uint[] { 2327 }, client.Sent);
    }

    [Fact]
    public async Task LoginSkipsTracksForARevisionWithoutThem()
    {
        var client = new TestClient(await Profile("NITRO-1-6-6"));
        client.SetHabbo(new Habbo { Id = 7 });
        var manager = await Manager(new FakeDatabase(), new BadgeDefinitions());

        manager.SendTracks(client);

        Assert.Empty(client.Sent);
    }

    [Fact]
    public async Task BadgePrizeIsGrantedInTheClaimTransaction()
    {
        var database = TrackDatabase();
        var (client, habbo, manager) = await Claimer(database, new BadgeDefinitions(Badge));

        await manager.Claim(client, "introduction", "track_champ");

        var write = Assert.Single(database.Committed);
        Assert.Contains(write, sql => sql.Contains("INTO users_reward_track_prizes"));
        Assert.Contains(write, sql => sql.Contains("INTO user_badges"));
        Assert.True(habbo.Inventory.Badges.HasBadge(Badge));
        Assert.Equal(RewardTrackResults.Ok, client.ClaimResult());
        Assert.True(client.Sent.IndexOf(9451) > client.Sent.IndexOf(ServerHeader(client, ServerPacketHeader.BadgesComposer)));
    }

    [Fact]
    public async Task PrizeWithoutABadgeDefinitionIsRejectedAndStaysClaimable()
    {
        var database = TrackDatabase();
        var badges = new BadgeDefinitions();
        var (client, habbo, manager) = await Claimer(database, badges);

        await manager.Claim(client, "introduction", "track_champ");

        Assert.Equal(RewardTrackResults.Unknown, client.ClaimResult());
        Assert.Empty(database.Committed);
        Assert.False(habbo.Inventory.Badges.HasBadge(Badge));

        badges.Add(Badge);
        await manager.Claim(client, "introduction", "track_champ");
        Assert.Equal(RewardTrackResults.Ok, client.ClaimResult());
        Assert.Single(database.Committed);
    }

    [Fact]
    public async Task BadgeLimitedToARightIsNotGrantedWithoutIt()
    {
        var database = TrackDatabase();
        var (client, habbo, manager) = await Claimer(database, new BadgeDefinitions((Badge, "badge_staff")));

        await manager.Claim(client, "introduction", "track_champ");

        Assert.Equal(RewardTrackResults.Unknown, client.ClaimResult());
        Assert.Empty(database.Committed);
        Assert.False(habbo.Inventory.Badges.HasBadge(Badge));
    }

    [Fact]
    public async Task FailedBadgeWriteCommitsNothingAndTheClaimCanBeRetried()
    {
        var database = TrackDatabase();
        database.FailOn = "INTO user_badges";
        var (client, habbo, manager) = await Claimer(database, new BadgeDefinitions(Badge));

        await manager.Claim(client, "introduction", "track_champ");

        Assert.Equal(RewardTrackResults.Unknown, client.ClaimResult());
        Assert.Empty(database.Committed);
        Assert.Equal(1, database.RolledBack);
        Assert.False(habbo.Inventory.Badges.HasBadge(Badge));
        Assert.DoesNotContain(ServerHeader(client, ServerPacketHeader.BadgesComposer), client.Sent);

        database.FailOn = null;
        await manager.Claim(client, "introduction", "track_champ");
        Assert.Equal(RewardTrackResults.Ok, client.ClaimResult());
        Assert.True(habbo.Inventory.Badges.HasBadge(Badge));
    }

    private static uint ServerHeader(TestClient client, uint internalId) => client.Revision.InternalIdToOutgoingIdMapping[internalId];

    // RevisionsCache.Start rewrites example.json; a private copy keeps it from racing tests that read the shared one.
    private static async Task<Revision> Profile(string name)
    {
        var directory = Directory.CreateTempSubdirectory("revisions-").FullName;

        try {
            foreach (var file in Directory.GetFiles(Path.Join(AppContext.BaseDirectory, "revisions"), "*.json")) {
                File.Copy(file, Path.Join(directory, Path.GetFileName(file)));
            }

            var cache = new RevisionsCache();
            typeof(RevisionsCache).GetField("_directory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cache, directory);
            await cache.Start();

            return cache.Revisions[name];
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<RewardTrackManager> Manager(FakeDatabase database, IBadgeManager badges)
    {
        var manager = new RewardTrackManager(NullLogger<RewardTrackManager>.Instance, database, badges, new FixedTimeProvider(FixedTimeProvider.Epoch));
        await manager.Start();

        return manager;
    }

    private static async Task<(TestClient Client, Habbo Habbo, RewardTrackManager Manager)> Claimer(FakeDatabase database, IBadgeManager badges)
    {
        var client = new TestClient(await Profile("OCTANE-3-6-0-FLOOR-20260909"));
        var habbo = new Habbo
        {
            Id = 7,
            Username = "player",
            Access = UserAccess.Empty,
            Inventory = new InventoryComponent { Badges = new BadgesInventoryComponent(new()) }
        };
        client.SetHabbo(habbo);

        return (client, habbo, await Manager(database, badges));
    }

    /// <summary>The introduction track's champ prize, with the player 10 points past it.</summary>
    private static FakeDatabase TrackDatabase()
    {
        var database = new FakeDatabase();
        database.Tables["FROM reward_tracks "] = Table(
            ("Id", "introduction"), ("Theme", "blue"), ("SortOrder", 0), ("StartsAt", DBNull.Value), ("EndsAt", DBNull.Value), ("HasPremium", 1),
            ("Boost", 1.0), ("InstantPoints", 0), ("CostDiamonds", 0), ("CostCredits", 25));
        database.Tables["FROM reward_track_prizes"] = Table(
            ("TrackId", "introduction"), ("Id", "track_champ"), ("RequiredPoints", 50), ("ProductItemTypeId", 4), ("RewardType", "badge"),
            ("ExtraParams", Badge), ("RewardAmount", 1), ("Premium", 0), ("SortOrder", 1));
        database.Tables["FROM users_reward_tracks "] = Table(("TrackId", "introduction"), ("Points", 60), ("Premium", 0));

        return database;
    }

    private static DataTable Table(params (string Column, object Value)[] row)
    {
        var table = new DataTable();

        foreach (var (column, value) in row) {
            table.Columns.Add(column, value is DBNull ? typeof(DateTimeOffset) : value.GetType());
        }

        table.Rows.Add(row.Select(cell => cell.Value).ToArray());

        return table;
    }

    private sealed class BadgeDefinitions : IBadgeManager
    {
        private readonly Dictionary<string, BadgeDefinition> _badges = new();

        public BadgeDefinitions(params string[] codes)
        {
            foreach (var code in codes) {
                Add(code);
            }
        }

        public BadgeDefinitions((string Code, string Right) badge) =>
            _badges[badge.Code.ToUpper()] = new() { Code = badge.Code, RequiredRight = badge.Right };

        public IReadOnlyDictionary<string, BadgeDefinition> Badges => _badges;
        public void Add(string code) => _badges[code.ToUpper()] = new() { Code = code };
        public Task Init() => Task.CompletedTask;
        public Task GiveBadge(Habbo habbo, string code) => throw new InvalidOperationException("Claims grant badges in their own transaction.");
        public Task RemoveBadge(Habbo habbo, string badge) => throw new NotSupportedException();
        public Task<List<Badge>> LoadBadgesForHabbo(int userId) => throw new NotSupportedException();
    }

    /// <summary>Serves canned SELECT results and records each committed transaction's statements.</summary>
    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return now;
        }
    }

    private sealed class FakeDatabase : IDatabase
    {
        public Dictionary<string, DataTable> Tables { get; } = new();
        public List<List<string>> Committed { get; } = new();
        public int RolledBack { get; set; }
        public string? FailOn { get; set; }
        public bool IsConnected() => true;
        public IDbConnection Connection() => new FakeConnection(this);

        public DataTable Select(string sql) => Tables.FirstOrDefault(entry => sql.Contains(entry.Key)).Value?.Copy() ?? new DataTable();
    }

    private sealed class FakeConnection(FakeDatabase database) : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        public FakeDatabase Store => database;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName)
        {
        }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new FakeTransaction(this);
        protected override DbCommand CreateDbCommand() => new FakeCommand { Connection = this };
    }

    private sealed class FakeTransaction(FakeConnection connection) : DbTransaction
    {
        private bool _done;
        public List<string> Statements { get; } = new();
        public override IsolationLevel IsolationLevel => IsolationLevel.RepeatableRead;
        protected override DbConnection DbConnection => connection;

        public override void Commit()
        {
            connection.Store.Committed.Add(Statements);
            _done = true;
        }

        public override void Rollback()
        {
            if (_done) {
                return;
            }

            connection.Store.RolledBack++;
            _done = true;
        }

        protected override void Dispose(bool disposing)
        {
            Rollback();
            base.Dispose(disposing);
        }
    }

    private sealed class FakeCommand : DbCommand
    {
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameters();
        protected override DbTransaction? DbTransaction { get; set; }
        private FakeDatabase Owner => ((FakeConnection)DbConnection!).Store;
        public override void Cancel()
        {
        }
        public override void Prepare()
        {
        }
        public override object? ExecuteScalar() => null;
        protected override DbParameter CreateDbParameter() => new FakeParameter();

        public override int ExecuteNonQuery()
        {
            if (Owner.FailOn != null && CommandText.Contains(Owner.FailOn)) {
                throw new InvalidOperationException("Injected write failure.");
            }

            if (DbTransaction is FakeTransaction transaction) {
                transaction.Statements.Add(CommandText);
            }
            else {
                Owner.Committed.Add(new() { CommandText });
            }

            return 1;
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => Owner.Select(CommandText).CreateDataReader();
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = "";
        public override int Size { get; set; }
        [AllowNull] public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override void ResetDbType()
        {
        }
    }

    private sealed class FakeParameters : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value)
        {
            _items.Add((DbParameter)value);

            return _items.Count - 1;
        }
        public override void AddRange(Array values)
        {
            foreach (var value in values) {
                Add(value);
            }
        }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
    }

    /// <summary>A Flash client on a real revision profile; records outgoing headers and bodies.</summary>
    private sealed class TestClient : GameClient
    {
        private readonly List<byte[]> _bodies = new();
        public List<uint> Sent { get; } = new();

        public TestClient(Revision revision) : base(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = revision;
            SendCallback = args =>
            {
                _bodies.Add(args.MemoryBuffer.Slice(6).ToArray());

                return false;
            };
        }

        /// <summary>Result code of the last RewardTrackClaimResult: string track, string prize, int result.</summary>
        public RewardTrackResults ClaimResult()
        {
            var body = _bodies[Sent.LastIndexOf(9451)];

            return (RewardTrackResults)BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(body.Length - 4));
        }

        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (true, false, 0, 0, 0);

        public override void CreateHeader(Memory<byte> memory, uint messageId) => Sent.Add(messageId);
    }
}
