using System.Reflection;
using System.Runtime.ExceptionServices;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

public sealed class MessengerPermissionLockDatabaseTests(MessengerFriendSchema schema) : IClassFixture<MessengerFriendSchema>
{
    [MessengerFriendDatabaseFact]
    public async Task OfflinePermissionResolutionCannotInvertRefreshAndAccountRowLocks()
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
        var options = new MySqlConnectionStringBuilder(schema.ConnectionString) { DefaultCommandTimeout = 3 };
        var database = new HabbiconDatabaseTests.TestDatabase(options.ConnectionString);
        using var observer = new MySqlConnection(schema.ConnectionString);
        observer.Open();
        observer.Execute("""
            INSERT INTO users (id,username,auth_ticket) VALUES (9981,'permission_acceptor',''),(9982,'permission_sender','');
            INSERT INTO users_settings (user_id) VALUES (9981),(9982);
            INSERT INTO messenger_requests (from_id,to_id) VALUES (9982,9981);
            """);
        var clients = new GameClientManager(null!, null!);
        var access = new AccessControl(database, clients, NullLogger<AccessControl>.Instance, TimeProvider.System);
        access.Init();
        var proxy = DispatchProxy.Create<IAccessControl, PermissionBarrier>();
        var barrier = (PermissionBarrier)(object)proxy;
        barrier.Access = access;
        Task? refresh = null;
        barrier.BeforeFirstResolve = () =>
        {
            refresh = Task.Run(() => access.Refresh(9981));
            // On the broken path the accept owns the users rows and Refresh waits while holding _sync.
            // On the fixed path Refresh finishes because permission resolution precedes the transaction.
            for (var attempt = 0; attempt < 25 && !refresh.IsCompleted; attempt++)
            {
                Thread.Sleep(200); // INNODB_TRX / LOCK_WAITS snapshots are cached for 100ms.
                if (observer.ExecuteScalar<int>("SELECT COUNT(*) FROM information_schema.INNODB_LOCK_WAITS") > 0) break;
            }
        };
        var settings = DispatchProxy.Create<ISettingsManager, OptionalSettings>();
        var loader = new MessengerDataLoader(database, clients, proxy, settings, TimeProvider.System);
        var accept = Task.Run(() => loader.AcceptFriendRequest(9981, 9982));
        try
        {
            var result = await accept.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotNull(refresh);
            await refresh.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Null(result.Error);
            Assert.Equal(2, observer.ExecuteScalar<int>("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id IN (9981,9982)"));
            Assert.Equal(0, observer.ExecuteScalar<int>("SELECT COUNT(*) FROM messenger_requests WHERE from_id=9982 AND to_id=9981"));
        }
        finally
        {
            try { await accept.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
            if (refresh != null) { try { await refresh.WaitAsync(TimeSpan.FromSeconds(15)); } catch { } }
            ((ITimer?)typeof(AccessControl).GetField("_expiryTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(access))?.Dispose();
        }
    }

    public class PermissionBarrier : DispatchProxy
    {
        public AccessControl Access = null!;
        public Action BeforeFirstResolve = null!;
        private bool _entered;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IAccessControl.Resolve) && !_entered)
            {
                _entered = true;
                BeforeFirstResolve();
            }
            try { return method.Invoke(Access, args); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
    }
    public class OptionalSettings : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(ISettingsManager.GetOptionalValue) ? null : throw new NotSupportedException(method.Name);
    }
}
