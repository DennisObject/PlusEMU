using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Plus.Communication.Http;
using Plus.Database;
using Plus.Database.Interfaces;
using Xunit;

namespace Plus.Tests;

/// <summary>
/// Runs only when PLUS_AUTH_TEST_DB holds a connection string to a disposable PlusEMU
/// database with Database/Migrations/20_SecureLoginTokens.sql applied.
/// </summary>
public sealed class AuthDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_AUTH_TEST_DB";

    public AuthDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to a disposable PlusEMU database to run.";
    }
}

internal sealed class AuthTestDatabase : IDatabase
{
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable(AuthDatabaseFactAttribute.Variable) ?? "";

    public bool IsConnected() => true;
    public IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
    public IDbConnection Connection() => new MySqlConnection(ConnectionString);

    public static int InsertUser(string username, string password = "", string mail = "probe@invalid")
    {
        using var connection = new MySqlConnection(ConnectionString);
        return connection.ExecuteScalar<int>("INSERT INTO users (username, password, mail) VALUES (@username, @password, @mail); SELECT LAST_INSERT_ID();",
            new { username, password, mail });
    }

    public static void DeleteUsers(IEnumerable<int> ids)
    {
        using var connection = new MySqlConnection(ConnectionString);
        connection.Execute("DELETE FROM user_access_tokens WHERE user_id IN @ids; DELETE FROM user_statistics WHERE id IN @ids; DELETE FROM users WHERE id IN @ids;",
            new { ids = ids.ToArray() });
    }

    public static string UniqueName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..(15 - prefix.Length)];
}

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

internal static class AuthTestConfig
{
    public static IOptions<AuthApiConfiguration> Options(Action<AuthApiConfiguration>? configure = null)
    {
        var configuration = new AuthApiConfiguration();
        configure?.Invoke(configuration);
        return Microsoft.Extensions.Options.Options.Create(configuration);
    }
}
