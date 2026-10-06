using System.Reflection;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Messenger;
using Plus.Core.Settings;
using Xunit;

namespace Plus.Tests;

public sealed class MessengerFriendDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_MESSENGER_FRIEND_SERVER_CONNECTION_STRING";
    public MessengerFriendDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) == null)
        {
            Skip = $"Set {Variable} to a bare MariaDB server connection (no Database).";
        }
    }
}

/// <summary>One GUID schema per class, loaded once from the pristine dump; every test uses its own account ids.</summary>
public sealed class MessengerFriendSchema : IDisposable
{
    private readonly string _server;
    public string Name { get; } = "task_messenger_tests_" + Guid.NewGuid().ToString("N")[..12];
    public string ConnectionString
    {
        get;
    }

    public MessengerFriendSchema()
    {
        _server = Environment.GetEnvironmentVariable(MessengerFriendDatabaseFactAttribute.Variable)!;

        using (var admin = new MySqlConnection(_server))
        {
            admin.Open();
            admin.Execute($"CREATE DATABASE `{Name}` CHARACTER SET utf8mb4");
        }

        ConnectionString = new MySqlConnectionStringBuilder(_server) { Database = Name, AllowZeroDateTime = true, ConvertZeroDateTime = true, SslMode = MySqlSslMode.None, Pooling = false, AllowUserVariables = true }.ToString();
        using var connection = new MySqlConnection(ConnectionString);
        connection.Open();
        connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql")), commandTimeout: 900);
    }

    public void Dispose()
    {
        using var admin = new MySqlConnection(_server);
        admin.Open();
        admin.Execute($"DROP DATABASE IF EXISTS `{Name}`");
    }
}

public sealed class MessengerFriendMutationDatabaseTests(MessengerFriendSchema schema) : IClassFixture<MessengerFriendSchema>
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private MessengerDataLoader Loader(IDatabase? database = null) =>
        new(database ?? new HabbiconDatabaseTests.TestDatabase(schema.ConnectionString), new GameClientManager(null!, null!), Permissions(), Settings(), Clock);

    private static IAccessControl Permissions()
    {
        var proxy = DispatchProxy.Create<IAccessControl, Forwarder>();
        ((Forwarder)(object)proxy).Handler = (method, _) => method.Name == nameof(IAccessControl.Resolve) ? UserAccess.Empty : throw new NotSupportedException(method.Name);

        return (IAccessControl)(object)proxy;
    }

    private static ISettingsManager Settings()
    {
        var proxy = DispatchProxy.Create<ISettingsManager, Forwarder>();
        ((Forwarder)(object)proxy).Handler = (method, _) => method.Name == nameof(ISettingsManager.GetOptionalValue) ? null : throw new NotSupportedException(method.Name);

        return (ISettingsManager)(object)proxy;
    }

    // Accounts are created with ids unique to each test so the shared schema never leaks state between tests.
    private void Account(int id)
    {
        using var connection = new MySqlConnection(schema.ConnectionString);
        connection.Open();
        connection.Execute("INSERT INTO users (id, username, auth_ticket, credits, activity_points, vip_points) VALUES (@id, @name, '', 0, 0, 0)", new
        {
            id,
            name = "friend_" + id
        });
        connection.Execute("INSERT IGNORE INTO users_settings (user_id) VALUES (@id)", new
        {
            id
        });
    }

    private static HabboMessenger MessengerFor(IEnumerable<MessengerBuddy>? friends = null, IEnumerable<MessengerRequest>? requests = null, IEnumerable<int>? outstanding = null) =>
        new(friends?.ToDictionary(friend => friend.Id) ?? [], requests?.ToDictionary(request => request.FromId) ?? [], (outstanding ?? []).ToList(), Clock);

    private static Habbo Habbo(int id, HabboMessenger messenger) => new() { Id = id, Username = "friend_" + id, Access = UserAccess.Empty, Messenger = messenger };

    private int Scalar(string sql, object? parameters = null)
    {
        using var connection = new MySqlConnection(schema.ConnectionString);
        connection.Open();

        return connection.ExecuteScalar<int>(sql, parameters);
    }

    private void Execute(string sql, object? parameters = null)
    {
        using var connection = new MySqlConnection(schema.ConnectionString);
        connection.Open();
        connection.Execute(sql, parameters);
    }

    [MessengerFriendDatabaseFact]
    public async Task AcceptCommitsBothRowsAndConsumesOnlyTheSendersRequest()
    {
        Account(9101);
        Account(9102);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9102, 9101), (9101, 9102)");
        var acceptor = Habbo(9101, MessengerFor(requests: [new MessengerRequest { FromId = 9102, ToId = 9101 }]));
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));

        Assert.Null(await service.AcceptRequestAsync(acceptor, 9102));

        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE (user_one_id = 9101 AND user_two_id = 9102) OR (user_one_id = 9102 AND user_two_id = 9101)"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9102 AND to_id = 9101"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9101 AND to_id = 9102"));
        Assert.NotNull(acceptor.Messenger.GetFriend(9102));
        Assert.False(acceptor.Messenger.Requests.ContainsKey(9102));
    }

    [MessengerFriendDatabaseFact]
    public async Task ConcurrentAcceptsOfOnePairCommitExactlyOnce()
    {
        Account(9201);
        Account(9202);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9202, 9201)");
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));
        var first = Habbo(9201, MessengerFor(requests: [new MessengerRequest { FromId = 9202, ToId = 9201 }]));
        var second = Habbo(9201, MessengerFor(requests: [new MessengerRequest { FromId = 9202, ToId = 9201 }]));

        var results = await Task.WhenAll(service.AcceptRequestAsync(first, 9202), service.AcceptRequestAsync(second, 9202));

        Assert.Single(results, result => result == null);
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE (user_one_id = 9201 AND user_two_id = 9202) OR (user_one_id = 9202 AND user_two_id = 9201)"));
    }

    [MessengerFriendDatabaseFact]
    public async Task MissingRequestOrAccountCommitsNothingAndLeavesMemoryAlone()
    {
        Account(9301);
        Account(9302);
        var acceptor = Habbo(9301, MessengerFor(requests: [new MessengerRequest { FromId = 9302, ToId = 9301 }]));
        var loader = Loader();

        var missing = await loader.AcceptFriendRequest(9301, 9302);
        Assert.Equal(FriendRequestError.NoFriendRequest, missing.Error);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id = 9301"));
        Assert.Empty(acceptor.Messenger.Friends);

        var unknown = await loader.AcceptFriendRequest(9301, 9399);
        Assert.Equal(FriendRequestError.NoFriendRequest, unknown.Error);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id = 9301"));
    }

    [MessengerFriendDatabaseFact]
    public async Task FullFriendListRollsBackTheConsumedRequest()
    {
        Account(9401);
        Account(9402);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9402, 9401)");

        for (var i = 0; i < 300; i++)
        {
            Execute("INSERT INTO messenger_friendships (user_one_id, user_two_id) VALUES (9401, @other)", new
            {
                other = 9500000 + i
            });
        }

        var acceptor = Habbo(9401, MessengerFor(requests: [new MessengerRequest { FromId = 9402, ToId = 9401 }]));
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));

        Assert.Equal(FriendRequestError.FriendLimitReached, await service.AcceptRequestAsync(acceptor, 9402));

        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9402 AND to_id = 9401"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id = 9402"));
        Assert.True(acceptor.Messenger.Requests.ContainsKey(9402));
        Assert.Empty(acceptor.Messenger.Friends.Where(pair => pair.Key == 9402));
    }

    [MessengerFriendDatabaseFact]
    public async Task DeclineDeletesOnlyTheSenderToRecipientRequest()
    {
        Account(9501);
        Account(9502);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9502, 9501), (9501, 9502)");
        var decliner = Habbo(9501, MessengerFor(requests: [new MessengerRequest { FromId = 9502, ToId = 9501 }]));
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));

        Assert.Null(await service.DeclineRequestAsync(decliner, 9502));

        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9502 AND to_id = 9501"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9501 AND to_id = 9502"));
        Assert.False(decliner.Messenger.Requests.ContainsKey(9502));
    }

    [MessengerFriendDatabaseFact]
    public async Task DecliningLegacyDuplicateRowsRemovesTheCachedRequestAfterTheirCommit()
    {
        Account(9891);
        Account(9892);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9892, 9891), (9892, 9891), (9891, 9892)");
        var decliner = Habbo(9891, MessengerFor(requests: [new MessengerRequest { FromId = 9892, ToId = 9891 }]));
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));

        Assert.Null(await service.DeclineRequestAsync(decliner, 9892));

        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9892 AND to_id = 9891"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9891 AND to_id = 9892"));
        Assert.False(decliner.Messenger.Requests.ContainsKey(9892));
    }

    [MessengerFriendDatabaseFact]
    public async Task RemoveDeletesBothDirectionsAndUpdatesBothMemories()
    {
        Account(9601);
        Account(9602);
        Execute("INSERT INTO messenger_friendships (user_one_id, user_two_id) VALUES (9601, 9602), (9602, 9601)");
        var friend = new MessengerBuddy { Id = 9602 };
        var remover = Habbo(9601, MessengerFor(friends: [friend]));
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));

        await service.RemoveFriendsAsync(remover, [9602]);

        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id IN (9601, 9602) AND user_two_id IN (9601, 9602)"));
        Assert.Null(remover.Messenger.GetFriend(9602));
    }

    [MessengerFriendDatabaseFact]
    public async Task RegisterDuplicateRequestReportsOutstanding()
    {
        Account(9701);
        Account(9702);
        var loader = Loader();

        Assert.True(await loader.RegisterFriendRequest(9701, 9702));
        Assert.False(await loader.RegisterFriendRequest(9701, 9702));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9701 AND to_id = 9702"));
    }

    [MessengerFriendDatabaseFact]
    public async Task StorageFailureLeavesMemoryAndPublicationUnchanged()
    {
        Account(9801);
        Account(9802);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9802, 9801)");
        var acceptor = Habbo(9801, MessengerFor(requests: [new MessengerRequest { FromId = 9802, ToId = 9801 }]));
        var failing = DispatchProxy.Create<IMessengerDataLoader, Forwarder>();
        var real = Loader();
        ((Forwarder)(object)failing).Handler = (method, args) => method.Name == nameof(IMessengerDataLoader.AcceptFriendRequest)
            ? throw new InvalidOperationException("Injected storage failure")
            : method.Invoke(real, args);
        var service = new MessengerFriendMutationService((IMessengerDataLoader)(object)failing, new AccountSessionGate(), new GameClientManager(null!, null!));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptRequestAsync(acceptor, 9802));

        Assert.True(acceptor.Messenger.Requests.ContainsKey(9802));
        Assert.Empty(acceptor.Messenger.Friends);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9802 AND to_id = 9801"));
    }

    [MessengerFriendDatabaseFact]
    public async Task SortedAccountGateWaitsForAHeldLoginAndNeverDeadlocks()
    {
        Account(9901);
        Account(9902);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9902, 9901)");
        var gate = new AccountSessionGate();
        var held = await gate.EnterAsync(9902);
        var service = new MessengerFriendMutationService(Loader(), gate, new GameClientManager(null!, null!));
        var acceptor = Habbo(9901, MessengerFor(requests: [new MessengerRequest { FromId = 9902, ToId = 9901 }]));

        var accept = service.AcceptRequestAsync(acceptor, 9902);

        try
        {
            await Task.Delay(200);
            Assert.False(accept.IsCompleted);
        }
        finally
        {
            held.Dispose();

            try
            {
                await accept.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception) { }
        }

        Assert.Null(await accept.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [MessengerFriendDatabaseFact]
    public async Task ReversePairOperationsInBothDirectionsCompleteWithoutDeadlock()
    {
        Account(9951);
        Account(9952);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9952, 9951), (9951, 9952)");
        var gate = new AccountSessionGate();
        var service = new MessengerFriendMutationService(Loader(), gate, new GameClientManager(null!, null!));
        var first = Habbo(9951, MessengerFor(requests: [new MessengerRequest { FromId = 9952, ToId = 9951 }]));
        var second = Habbo(9952, MessengerFor(requests: [new MessengerRequest { FromId = 9951, ToId = 9952 }]));

        var tasks = Task.WhenAll(service.AcceptRequestAsync(first, 9952), service.AcceptRequestAsync(second, 9951));

        await tasks.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE (user_one_id = 9951 AND user_two_id = 9952) OR (user_one_id = 9952 AND user_two_id = 9951)"));
    }

    [MessengerFriendDatabaseFact]
    public async Task FailureAfterTheRelationshipInsertRollsBackTheConsumedRequest()
    {
        Account(9981);
        Account(9982);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9982, 9981)");
        Execute("CREATE TRIGGER messenger_test_fail_friend BEFORE INSERT ON messenger_friendships FOR EACH ROW BEGIN IF NEW.user_one_id = 9981 AND NEW.user_two_id = 9982 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'injected friendship failure'; END IF; END");

        try
        {
            var acceptor = Habbo(9981, MessengerFor(requests: [new MessengerRequest { FromId = 9982, ToId = 9981 }]));
            var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));

            await Assert.ThrowsAnyAsync<Exception>(() => service.AcceptRequestAsync(acceptor, 9982));

            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9982 AND to_id = 9981"));
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id IN (9981, 9982)"));
            Assert.True(acceptor.Messenger.Requests.ContainsKey(9982));
            Assert.Empty(acceptor.Messenger.Friends);
        }
        finally { Execute("DROP TRIGGER IF EXISTS messenger_test_fail_friend"); }
    }

    [MessengerFriendDatabaseFact]
    public async Task SelfAndMissingAccountPairsAreRefusedBeforeAnyWrite()
    {
        Account(9991);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9991, 9991)");
        var loader = Loader();

        Assert.Equal(FriendRequestError.NoFriendRequest, (await loader.AcceptFriendRequest(9991, 9991)).Error);
        Assert.False(await loader.RegisterFriendRequest(9991, 9991));
        Assert.False(await loader.RegisterFriendRequest(9991, 9999991));
        Assert.Equal(0, await loader.DeleteFriendship(9991, 9991));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9991 AND to_id = 9991"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE to_id = 9999991"));
    }

    [MessengerFriendDatabaseFact]
    public async Task SameStripeAccountsAcceptWithoutSelfDeadlock()
    {
        // 9xxx1 and 9xxx65 share a stripe (id mod 64); the canonical seam must hold that stripe once.
        Account(9311);
        Account(9375);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9375, 9311)");
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(TimeSpan.FromSeconds(5)), new GameClientManager(null!, null!));
        var acceptor = Habbo(9311, MessengerFor(requests: [new MessengerRequest { FromId = 9375, ToId = 9311 }]));

        Assert.Null(await service.AcceptRequestAsync(acceptor, 9375).WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE (user_one_id = 9311 AND user_two_id = 9375) OR (user_one_id = 9375 AND user_two_id = 9311)"));
    }

    [MessengerFriendDatabaseFact]
    public async Task OpposingPairsAcrossWraparoundStripesCompleteWithoutDeadlock()
    {
        // The two accepted pairs are driven in opposing directions over shared accounts while a third account removes a friend.
        Account(9711);
        Account(9712);
        Account(9713);
        Account(9714);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9712, 9711), (9714, 9713)");
        var gate = new AccountSessionGate(TimeSpan.FromSeconds(20));
        var service = new MessengerFriendMutationService(Loader(), gate, new GameClientManager(null!, null!));
        var first = Habbo(9711, MessengerFor(requests: [new MessengerRequest { FromId = 9712, ToId = 9711 }]));
        var second = Habbo(9713, MessengerFor(requests: [new MessengerRequest { FromId = 9714, ToId = 9713 }]));
        var third = Habbo(9714, MessengerFor(requests: []));

        var tasks = Task.WhenAll(service.AcceptRequestAsync(first, 9712), service.AcceptRequestAsync(second, 9714), service.RemoveFriendsAsync(third, [9713]));
        await tasks.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE (user_one_id = 9711 AND user_two_id = 9712) OR (user_one_id = 9712 AND user_two_id = 9711)"));
    }

    // Forwards every member to the real object unless a test overrides it.
    [MessengerFriendDatabaseFact]
    public async Task FailureWhileWritingTheSecondRelationshipRowRollsBackTheRequestAndTheFirstRow()
    {
        // The requester-to-acceptor row is inserted second; the signal aborts the statement after its insert, before any buddy read.
        Account(9811);
        Account(9812);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9812, 9811)");
        Execute("CREATE TRIGGER messenger_test_fail_second AFTER INSERT ON messenger_friendships FOR EACH ROW BEGIN IF NEW.user_one_id = 9812 AND NEW.user_two_id = 9811 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'injected second-row failure'; END IF; END");

        try
        {
            var acceptor = Habbo(9811, MessengerFor(requests: [new MessengerRequest { FromId = 9812, ToId = 9811 }]));
            var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));

            await Assert.ThrowsAnyAsync<Exception>(() => service.AcceptRequestAsync(acceptor, 9812));

            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9812 AND to_id = 9811"));
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id IN (9811, 9812)"));
            Assert.True(acceptor.Messenger.Requests.ContainsKey(9812));
            Assert.Empty(acceptor.Messenger.Friends);
        }
        finally { Execute("DROP TRIGGER IF EXISTS messenger_test_fail_second"); }
    }

    [MessengerFriendDatabaseFact]
    public async Task BuddyPreparationFailureAfterBothRelationshipRowsRollsBackEveryRow()
    {
        // The requester's users row is removed after the second relationship row, so the requester's buddy view reads nothing inside the transaction.
        Account(9851);
        Account(9852);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9852, 9851)");
        Execute("CREATE TRIGGER messenger_test_drop_requester AFTER INSERT ON messenger_friendships FOR EACH ROW BEGIN IF NEW.user_one_id = 9852 AND NEW.user_two_id = 9851 THEN DELETE FROM users WHERE id = 9852; END IF; END");

        try
        {
            var acceptor = Habbo(9851, MessengerFor(requests: [new MessengerRequest { FromId = 9852, ToId = 9851 }]));
            var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(), new GameClientManager(null!, null!));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptRequestAsync(acceptor, 9852));

            Assert.Equal("Accepted friend view missing.", error.Message);
            Assert.Equal(2, Scalar("SELECT COUNT(*) FROM users WHERE id IN (9851, 9852)"));
            Assert.Equal(2, Scalar("SELECT COUNT(*) FROM users_settings WHERE user_id IN (9851, 9852)"));
            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9852 AND to_id = 9851"));
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id IN (9851, 9852) OR user_two_id IN (9851, 9852)"));
            Assert.True(acceptor.Messenger.Requests.ContainsKey(9852));
            Assert.Empty(acceptor.Messenger.Friends);
        }
        finally { Execute("DROP TRIGGER IF EXISTS messenger_test_drop_requester"); }
    }

    [MessengerFriendDatabaseFact]
    public async Task AcceptingIsDecidedUnderTheHoldWhenAnIncomingRequestArrivesWhileWaiting()
    {
        Account(9821);
        Account(9822);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9822, 9821)");
        var gate = new AccountSessionGate(TimeSpan.FromSeconds(20));
        var service = new MessengerFriendMutationService(Loader(), gate, new GameClientManager(null!, null!));
        var sender = Habbo(9821, MessengerFor());
        var held = await gate.EnterManyAsync([9821, 9822]);
        Task<FriendRequestOutcome>? send = null;

        try
        {
            // The precheck sees no incoming request; the request is recorded in memory before the hold is released.
            send = service.SendRequestAsync(sender, 9822);
            await Task.Delay(200);
            Assert.False(send.IsCompleted);
            sender.Messenger.AddFriendRequest(new MessengerRequest { FromId = 9822, ToId = 9821 });
        }
        finally
        {
            // Release the lease even when an assertion fails, then observe the send so its exception is never unobserved.
            held.Dispose();

            if (send != null)
            {
                try
                {
                    await send.WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (Exception) { }
            }
        }

        var outcome = await send!;
        Assert.Null(outcome.Error);
        Assert.True(outcome.Accepted);
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE (user_one_id = 9821 AND user_two_id = 9822) OR (user_one_id = 9822 AND user_two_id = 9821)"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9822 AND to_id = 9821"));
    }

    [MessengerFriendDatabaseFact]
    public async Task ConcurrentDeclinesOfOneRequestCommitExactlyOnce()
    {
        Account(9831);
        Account(9832);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9832, 9831)");
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(TimeSpan.FromSeconds(20)), new GameClientManager(null!, null!));
        var first = Habbo(9831, MessengerFor(requests: [new MessengerRequest { FromId = 9832, ToId = 9831 }]));
        var second = Habbo(9831, MessengerFor(requests: [new MessengerRequest { FromId = 9832, ToId = 9831 }]));

        var results = await Task.WhenAll(service.DeclineRequestAsync(first, 9832), service.DeclineRequestAsync(second, 9832));

        Assert.Single(results, result => result == null);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_requests WHERE from_id = 9832 AND to_id = 9831"));
    }

    [MessengerFriendDatabaseFact]
    public async Task ConcurrentRemovalsOfOneFriendshipUpdateMemoryOnce()
    {
        Account(9841);
        Account(9842);
        Execute("INSERT INTO messenger_friendships (user_one_id, user_two_id) VALUES (9841, 9842), (9842, 9841)");
        var remover = Habbo(9841, MessengerFor(friends: [new MessengerBuddy { Id = 9842 }]));
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(TimeSpan.FromSeconds(20)), new GameClientManager(null!, null!));

        await Task.WhenAll(service.RemoveFriendsAsync(remover, [9842]), service.RemoveFriendsAsync(remover, [9842]));

        Assert.Null(remover.Messenger.GetFriend(9842));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id IN (9841, 9842) AND user_two_id IN (9841, 9842)"));
    }

    [MessengerFriendDatabaseFact]
    public void PristineSchemaHasNoRequestPairKeyButFriendshipsArePairKeyed()
    {
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'messenger_requests' AND INDEX_NAME <> 'PRIMARY' AND NON_UNIQUE = 0"));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'messenger_friendships' AND INDEX_NAME = 'PRIMARY'"));
    }

    [MessengerFriendDatabaseFact]
    public async Task StagedGapWriteFromAnotherPairCompletesWithoutDeadlock()
    {
        // A repeatable-read gap held on a missing pair past every existing friendship must not deadlock an unrelated accept whose insert lands in that gap.
        Account(9961);
        Account(9962);
        Account(9971);
        Account(9972);
        Execute("INSERT INTO messenger_requests (from_id, to_id) VALUES (9972, 9971)");
        var service = new MessengerFriendMutationService(Loader(), new AccountSessionGate(TimeSpan.FromSeconds(20)), new GameClientManager(null!, null!));
        var acceptor = Habbo(9971, MessengerFor(requests: [new MessengerRequest { FromId = 9972, ToId = 9971 }]));
        using var holder = new MySqlConnection(schema.ConnectionString);
        holder.Open();
        using var held = holder.BeginTransaction();
        Task<FriendRequestError?>? accept = null;

        try
        {
            holder.Execute("SELECT 1 FROM messenger_friendships WHERE user_one_id = 9961 AND user_two_id = 9962 FOR UPDATE", transaction: held);
            accept = service.AcceptRequestAsync(acceptor, 9972);
            await WaitForFriendshipLockWait(accept);
            holder.Execute("INSERT INTO messenger_friendships (user_one_id, user_two_id) VALUES (9961, 9962)", transaction: held);
            held.Commit();
            Assert.Null(await accept.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            try
            {
                held.Rollback();
            }
            catch (Exception) { }

            if (accept != null)
            {
                try
                {
                    await accept.WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (Exception) { }
            }
        }

        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM messenger_friendships WHERE (user_one_id = 9971 AND user_two_id = 9972) OR (user_one_id = 9972 AND user_two_id = 9971)"));
    }

    // The accept's own insert shows as a lock wait; if the accept finishes first, its real outcome is surfaced instead.
    // INNODB_TRX is a cached snapshot refreshed only after 100 ms without a read, so each sample is spaced 200 ms apart.
    private async Task WaitForFriendshipLockWait(Task accept)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (Scalar("SELECT COUNT(*) FROM information_schema.INNODB_TRX WHERE trx_state = 'LOCK WAIT' AND trx_query LIKE 'INSERT INTO messenger_friendships%SELECT 9971, 9972%'") > 0)
            {
                return;
            }

            if (accept.IsCompleted)
            {
                await accept;
                throw new InvalidOperationException("The accept finished without waiting on the held friendship gap.");
            }

            await Task.Delay(200);
        }

        await Task.Delay(200);
        using var inspector = new MySqlConnection(schema.ConnectionString);
        inspector.Open();
        var transactions = inspector.Query<string>("SELECT CONCAT(trx_id, ' ', trx_state, ' ', trx_mysql_thread_id, ' | ', LEFT(IFNULL(trx_query, ''), 140)) FROM information_schema.INNODB_TRX");
        var waits = inspector.Query<string>("SELECT CONCAT(requesting_trx_id, ' waits for ', blocking_trx_id) FROM information_schema.INNODB_LOCK_WAITS");
        throw new TimeoutException("The accept never waited on the held friendship gap. Open InnoDB transactions: " + string.Join("; ", transactions) + ". Lock waits: " + string.Join("; ", waits));
    }

    public class Forwarder : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (method, args) => method.Invoke(null, args);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
