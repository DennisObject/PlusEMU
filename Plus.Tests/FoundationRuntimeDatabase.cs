using System.Diagnostics;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Xunit;

namespace Plus.Tests;

internal static class FoundationRuntimeDatabase
{
    internal static async Task Run(Func<IDatabase, MySqlConnection, Task> test)
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FOUNDATION_RUNTIME_DATABASE"))
        {
            Database = "mysql",
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true,
            AllowUserVariables = true
        };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "foundation_runtime_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");

        try {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            // Includes the corrected update sequence appended to the shipped dump.
            await ImportPristine(options);
            AssertShippedShapes(connection);
            await test(new HabbiconDatabaseTests.TestDatabase(options.ConnectionString), connection);
        }
        finally {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static async Task ImportPristine(MySqlConnectionStringBuilder options)
    {
        Assert.True(Path.IsPathRooted(options.Server), "The smoke fixture requires an explicitly supplied Unix socket.");
        var start = new ProcessStartInfo("mariadb")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in new[] { "--protocol=SOCKET", "--socket=" + options.Server, "--user=" + options.UserID,
                     "--database=" + options.Database, "--default-character-set=utf8mb4" }) {
            start.ArgumentList.Add(argument);
        }

        start.Environment["MYSQL_PWD"] = options.Password;
        using var import = Process.Start(start)!;
        var stdout = import.StandardOutput.ReadToEndAsync();
        var stderr = import.StandardError.ReadToEndAsync();

        try {
            await using (var pristine = File.OpenRead(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"))) {
                await pristine.CopyToAsync(import.StandardInput.BaseStream);
            }

            import.StandardInput.Close();
            await import.WaitForExitAsync();
            var errors = await stderr;
            await stdout;
            Assert.True(import.ExitCode == 0, errors);
        }
        finally {
            if (!import.HasExited) {
                import.Kill(entireProcessTree: true);
                await import.WaitForExitAsync();
            }
        }
    }

    private static void AssertShippedShapes(MySqlConnection connection)
    {
        Assert.Equal("tinyint(1)", Column(connection, "rooms", "lay_enabled"));
        Assert.NotNull(Column(connection, "users", "bubble_id"));
        Assert.NotNull(Column(connection, "users", "auth_ticket_expires_at"));
        Assert.NotNull(Column(connection, "user_statistics", "id"));
        Assert.NotNull(Column(connection, "roles", "id"));
        Assert.NotNull(Column(connection, "user_club_memberships", "started_at"));
        Assert.Equal("datetime", Column(connection, "users", "account_created"));
        Assert.Equal("datetime", Column(connection, "users", "last_online"));
        Assert.Equal("datetime", Column(connection, "user_info", "trading_locked"));
        Assert.Equal("datetime", Column(connection, "user_effects", "activated_stamp"));
        Assert.Equal("enum('false','true')", Column(connection, "bots", "automatic_chat"));
        Assert.Equal("int(10) unsigned", Column(connection, "bots", "id"));
        Assert.Equal("datetime(6)", Column(connection, "bots_petdata", "createstamp"));
        Assert.Equal("varchar(25)", Column(connection, "user_clothing", "part_id"));
    }

    private static string? Column(MySqlConnection connection, string table, string column) => connection.QuerySingleOrDefault<string>(
        "SELECT COLUMN_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME=@column",
        new { table, column });
}
