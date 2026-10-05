using System.Data;
using Plus.Database;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class CredentialLifetimeTests
{
    [Fact]
    public async Task ExpiryOverflowIsRejectedBeforeAnyCredentialWrite()
    {
        var database = new CountingDatabase();
        var clock = new FixedTimeProvider(DateTimeOffset.MaxValue - TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new AccessTokenStore(database, clock, AuthTestConfig.Options(c => c.AccessTokenLifetimeMinutes = 1)).Issue(1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new RememberTokenStore(database, clock, AuthTestConfig.Options(c => c.RememberTokenLifetimeDays = 1)).Issue(1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new SsoTicketStore(database, clock, AuthTestConfig.Options(c => c.SsoTicketLifetimeSeconds = 60)).Issue(1));

        Assert.Equal(0, database.Connections);
    }

    [Fact]
    public void InvalidLifetimesAreRejectedBeforeOpeningTheDatabase()
    {
        var database = new CountingDatabase();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccessTokenStore(database, TimeProvider.System, AuthTestConfig.Options(c => c.AccessTokenLifetimeMinutes = 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RememberTokenStore(database, TimeProvider.System, AuthTestConfig.Options(c => c.RememberTokenLifetimeDays = 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SsoTicketStore(database, TimeProvider.System, AuthTestConfig.Options(c => c.SsoTicketLifetimeSeconds = 0)));

        Assert.Equal(0, database.Connections);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingDatabase : IDatabase
    {
        public int Connections { get; private set; }
        public bool IsConnected() => true;
        public IDbConnection Connection()
        {
            Connections++;
            throw new InvalidOperationException("A connection must not be opened.");
        }
    }
}
