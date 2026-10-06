using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Moderation;
using Xunit;

namespace Plus.Tests;

[Collection(AuthDatabaseFactAttribute.Collection)]
public sealed class BanLookupDatabaseTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2040, 2, 3, 4, 5, 6, TimeSpan.Zero);
    private readonly string _value = "ban" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _address = $"198.18.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 255)}";
    private readonly BanLookup _lookup = new(new AuthTestDatabase(), new FixedClock(Now));

    private void Ban(string type, string value, string reason, DateTimeOffset expire)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        connection.Execute("INSERT INTO bans (bantype, value, reason, expire, added_by, added_date) VALUES (@type, @value, @reason, @expire, 'probe', NULL)",
            new { type, value, reason, expire = expire.UtcDateTime });
    }

    private static DateTimeOffset In(TimeSpan span) => Now + span;

    [AuthDatabaseFact]
    public async Task FindsAnActiveIpBanForTheCallersAddress()
    {
        Ban("ip", _address, "Botting", In(TimeSpan.FromDays(1)));

        var ban = await _lookup.Find(_value, _address);

        Assert.Equal("Botting", ban?.Reason);
        Assert.Equal(In(TimeSpan.FromDays(1)), ban!.ExpiresAt);
    }

    [AuthDatabaseFact]
    public async Task FindsAnActiveUsernameBanCaseInsensitively()
    {
        Ban("user", _value, "Scamming", In(TimeSpan.FromHours(1)));

        Assert.Equal("Scamming", (await _lookup.Find(_value.ToUpperInvariant(), "192.0.2.1"))?.Reason);
    }

    [AuthDatabaseFact]
    public async Task IgnoresExpiredBansAndOtherBanTypes()
    {
        Ban("ip", _address, "old", In(TimeSpan.FromHours(-1)));
        Ban("machine", _value, "machine", In(TimeSpan.FromDays(1)));
        Ban("user", _address, "user named like an address", In(TimeSpan.FromDays(1)));

        Assert.Null(await _lookup.Find(_value, _address));
    }

    public void Dispose()
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        connection.Execute("DELETE FROM bans WHERE value IN (@_value, @_address)", new { _value, _address });
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
