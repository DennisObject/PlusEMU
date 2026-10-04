using System.Data;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Navigator;
using Plus.Database;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

[CollectionDefinition("RoomCreationAccess", DisableParallelization = true)]
public sealed class RoomCreationAccessCollection;

// RoomFactory uses the static database; isolate its replacement from every other test collection.
[Collection("RoomCreationAccess")]
public sealed class RoomModelAccessTests
{
    private const string ExtraPermission = PermissionKeys.RoomOwnerAny;
    private static readonly FieldInfo DatabaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;

    public static TheoryData<int, bool, string?, bool> Cases => new()
    {
        { 0, false, null, true },
        { 1, false, null, true },
        { 2, false, null, true },
        { 3, false, null, false },
        { -1, false, null, false },
        { -1, true, null, true },
        { 0, true, ExtraPermission, false },
        { -1, true, ExtraPermission, false }
    };

    public static TheoryData<int, bool, string?> DeniedCases => new()
    {
        { 3, false, null },
        { -1, false, null },
        { 0, true, ExtraPermission },
        { -1, true, ExtraPermission }
    };

    private static RoomModel Model(int requiredClubLevel, string? requiredPermission = null) =>
        new("test_model", 0, 0, 0, 0, "00\r00", requiredClubLevel, 0, false)
        {
            RequiredPermission = requiredPermission
        };

    private static UserAccess Access(bool staffModels = false, IEnumerable<UserPermissionOverride>? overrides = null, DateTimeOffset? expiresAt = null) =>
        UserAccess.Create([new RoleAssignment(new AccessRole(1, "test", "Test", 1000, 7, "", true,
            staffModels ? [PermissionKeys.NavigatorRoomModelsStaff, PermissionKeys.ClubAccess] : [PermissionKeys.ClubAccess], new Dictionary<string, int>()), expiresAt)], overrides);

    [Theory]
    [MemberData(nameof(Cases))]
    public void ModelRequirementsUseHotelClubPolicyAndPermissions(int requiredClubLevel, bool staffModels, string? requiredPermission, bool allowed)
    {
        Assert.Equal(allowed, Model(requiredClubLevel, requiredPermission).CanCreate(Access(staffModels)));
    }

    [Fact]
    public void ClubModelsAreDeniedWithoutMembershipOrAfterComplimentaryRoleExpiry()
    {
        Assert.False(Model(2).CanCreate(UserAccess.Empty));
        Assert.False(Model(2).CanCreate(Access(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1))));
    }

    [Fact]
    public void AnAdditionalModelPermissionIsRequiredAlongsideClubMembership()
    {
        var access = Access(overrides: [new UserPermissionOverride(ExtraPermission, false)]);

        Assert.True(Model(2, ExtraPermission).CanCreate(access));
        Assert.False(Model(3, ExtraPermission).CanCreate(access));
    }

    [Fact]
    public void DeniesAndExpiredMembershipsCannotAuthorizeModels()
    {
        Assert.False(Model(-1).CanCreate(Access(true, [new UserPermissionOverride(PermissionKeys.NavigatorRoomModelsStaff, true)])));
        Assert.False(Model(-1).CanCreate(Access(true, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1))));
    }

    [Theory]
    [MemberData(nameof(DeniedCases))]
    public async Task CreatePacketRejectsUnavailableModelsBeforeDatabaseAndCreation(int requiredClubLevel, bool staffModels, string? requiredPermission)
    {
        int modelReads = 0;
        var model = Model(requiredClubLevel, requiredPermission);
        var rooms = Proxy<IRoomManager>((method, arguments) =>
        {
            Assert.Equal("TryGetModel", method);
            modelReads++;
            arguments[1] = model;
            return true;
        });
        var navigator = Proxy<INavigatorManager>((_, _) => throw new InvalidOperationException("Navigator touched by denied request"));
        var previousDatabase = DatabaseField.GetValue(null);
        DatabaseField.SetValue(null, EditorTestSupport.UntouchableDatabase());
        try
        {
            var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7001, Access = Access(staffModels) });
            await new CreateFlatEvent(Filter(), rooms, navigator, Proxy<Plus.Core.Settings.ISettingsManager>((_, _) => null)).Parse(client, Request());

            Assert.Equal(1, modelReads);
            Assert.Empty(sent);
        }
        finally { DatabaseField.SetValue(null, previousDatabase); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(-1, true)]
    public async Task AccessibleModelsProceedThroughTheExistingCreationPath(int requiredClubLevel, bool staffModels)
    {
        var model = Model(requiredClubLevel);
        int creationCalls = 0, databaseReads = 0, friendUpdates = 0;
        var rooms = Proxy<IRoomManager>((method, arguments) =>
        {
            if (method == "TryGetModel")
            {
                arguments[1] = model;
                return true;
            }
            Assert.Equal("CreateRoom", method);
            creationCalls++;
            Assert.Equal(model, arguments[6]);
            Assert.Equal(36, arguments[3]); // Unknown categories keep the existing fallback.
            Assert.Equal(10, arguments[4]);
            Assert.Equal(0, arguments[5]);
            return null; // Room persistence belongs to RoomManager; this test observes the call boundary.
        });
        var navigator = Proxy<INavigatorManager>((method, _) => method == "TryGetSearchResultList" ? false : throw new InvalidOperationException(method));
        var database = ReaderDatabase(new DataTable(), () => databaseReads++);
        var habbo = new Habbo { Id = 7001, Access = Access(staffModels), Messenger = new HabboMessenger(new(), new(), new()) };
        habbo.Messenger.StatusUpdated += (_, _) => friendUpdates++;
        var previousDatabase = DatabaseField.GetValue(null);
        DatabaseField.SetValue(null, database);
        try
        {
            var (client, _) = HabbiconTestSupport.Client(habbo);
            await new CreateFlatEvent(Filter(), rooms, navigator, Proxy<Plus.Core.Settings.ISettingsManager>((_, _) => null)).Parse(client, Request());

            Assert.Equal(1, creationCalls);
            Assert.Equal(1, databaseReads);
            Assert.Equal(1, friendUpdates);
        }
        finally { DatabaseField.SetValue(null, previousDatabase); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagerLoadsAuthoritativeRequirementsForStandardAndCustomModels(bool custom)
    {
        var data = new DataTable();
        foreach (var name in new[] { "Id", "DoorX", "DoorY", "DoorZ", "DoorDir", "Heightmap", "WallHeight", "RequiredClubLevel", "RequiredPermission" })
            data.Columns.Add(name, name == "DoorZ" ? typeof(double) : name is "Id" or "Heightmap" or "RequiredPermission" ? typeof(string) : typeof(int));
        data.Rows.Add("test_model", 0, 0, 0d, 0, "00\r00", 0, -1, ExtraPermission);
        var manager = new RoomManager(NullLogger<RoomManager>.Instance, ReaderDatabase(data), null!, TimeProvider.System);
        if (custom)
            Assert.True(manager.LoadModel("test_model"));
        else
            manager.LoadModels();

        Assert.True(manager.TryGetModel("test_model", out var model));
        Assert.Equal(-1, model.RequiredClubLevel);
        Assert.Equal(ExtraPermission, model.RequiredPermission);
        Assert.False(model.CanCreate(Access(true)));
        Assert.True(model.CanCreate(Access(true, [new UserPermissionOverride(ExtraPermission, false)])));
    }

    private static IDatabase ReaderDatabase(DataTable data, Action? read = null)
    {
        return Proxy<IDatabase>((method, _) => method == "Connection" ? Connection() : throw new InvalidOperationException(method));

        IDbConnection Connection()
        {
            var state = ConnectionState.Closed;
            return Proxy<IDbConnection>((method, _) => method switch
            {
                "get_State" => state,
                "get_ConnectionString" => "room-model-memory",
                "Open" => Change(ConnectionState.Open),
                "Close" or "Dispose" => Change(ConnectionState.Closed),
                "CreateCommand" => Command(),
                _ => throw new NotSupportedException(method)
            });
            object? Change(ConnectionState next) { state = next; return null; }
        }

        IDbCommand Command()
        {
            var parameters = new MySqlCommand().Parameters;
            string sql = "";
            return Proxy<IDbCommand>((method, args) =>
            {
                switch (method)
                {
                    case "set_CommandText": sql = (string)args[0]!; return null;
                    case "get_CommandText": return sql;
                    case "get_Parameters": return parameters;
                    case "CreateParameter": return new MySqlParameter();
                    case "set_CommandTimeout": case "set_CommandType": case "Dispose": return null;
                    case "ExecuteReader": read?.Invoke(); return data.CreateDataReader();
                    default: throw new NotSupportedException(method);
                }
            });
        }
    }

    private static IWordFilterManager Filter() => Proxy<IWordFilterManager>((method, arguments) =>
        method == "CheckMessage" ? arguments[0] : throw new InvalidOperationException(method));

    private static Plus.Communication.Flash.FlashIncomingPacket Request() =>
        EditorTestSupport.Incoming("Test room", "Description", "test_model", 999, 99, 99);

    private static T Proxy<T>(Func<string, object?[], object?> callback) where T : class
    {
        var proxy = DispatchProxy.Create<T, Callback>();
        ((Callback)(object)proxy).InvokeCallback = callback;
        return proxy;
    }

    public class Callback : DispatchProxy
    {
        public Func<string, object?[], object?> InvokeCallback { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeCallback(targetMethod!.Name, args!);
    }
}
