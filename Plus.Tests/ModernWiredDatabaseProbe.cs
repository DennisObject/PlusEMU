using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Xunit;
namespace Plus.Tests;

// Every SQL probe first proves the disposable Compose container/network/volume identity.
internal static class ModernWiredDatabaseProbe
{
    public static string GuardedConnectionString()
    {
        using var process = Process.Start(new ProcessStartInfo("docker")
        {
            ArgumentList = { "inspect", "plus-wired-preview-db-1" },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var inspect = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        using var json = JsonDocument.Parse(inspect);
        var container = json.RootElement[0];
        Assert.Equal("plus-wired-preview", container.GetProperty("Config").GetProperty("Labels").GetProperty("com.docker.compose.project").GetString());
        var networks = container.GetProperty("NetworkSettings").GetProperty("Networks");
        Assert.Single(networks.EnumerateObject());
        Assert.True(networks.TryGetProperty("plus-wired-preview_default", out var network));
        Assert.Contains(container.GetProperty("Mounts").EnumerateArray(), m => m.TryGetProperty("Name", out var name) && name.GetString() == "plus-wired-preview_wired-db-data" && m.GetProperty("Destination").GetString() == "/var/lib/mysql");
        using var config = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("WIRED_VARIABLE_PREVIEW_CONFIG")!));
        var db = config.RootElement.GetProperty("Database");

        return new MySqlConnectionStringBuilder
        {
            Server = network.GetProperty("IPAddress").GetString(),
            Port = 3306,
            Database = db.GetProperty("Name").GetString(),
            UserID = db.GetProperty("Username").GetString(),
            Password = db.GetProperty("Password").GetString(),
            MinimumPoolSize = 0,
            MaximumPoolSize = 8
        }.ConnectionString;
    }

    public static uint Insert(MySqlConnection connection, string table, Dictionary<string, object> values)
    {
        var columns = connection.Query<Column>("SELECT COLUMN_NAME AS Name,DATA_TYPE AS Type,COLUMN_TYPE AS FullType FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND IS_NULLABLE='NO' AND COLUMN_DEFAULT IS NULL AND EXTRA NOT LIKE '%auto_increment%'", new
        {
            table
        });

        foreach (var column in columns.Where(x => !values.ContainsKey(x.Name)))
        {
            values[column.Name] = column.Type switch
            {
                "enum" => column.FullType.Split('\'')[1],
                "datetime" or "timestamp" or "date" => DateTime.UtcNow,
                "varchar" or "char" or "text" or "mediumtext" or "longtext" => "",
                _ => 0
            };
        }

        var parameters = new DynamicParameters();

        foreach (var value in values)
        {
            parameters.Add(value.Key, value.Value);
        }

        connection.Execute($"INSERT INTO `{table}` ({string.Join(',', values.Keys.Select(x => $"`{x}`"))}) VALUES ({string.Join(',', values.Keys.Select(x => "@" + x))})", parameters);

        return connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()");
    }
    private sealed class Column
    {
        public string Name { get; set; } = ""; public string Type { get; set; } = ""; public string FullType { get; set; } = "";
    }
    public sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public int Commands;
        public Action? BeforeConnection;
        public string? FailSqlPrefix;
        public bool IsConnected() => true;
        public IDbConnection Connection()
        {
            var action = BeforeConnection;
            BeforeConnection = null;
            action?.Invoke();

            return new CountedConnection(new MySqlConnection(connectionString), () => Interlocked.Increment(ref Commands), sql =>
            {
                if (FailSqlPrefix is { } prefix && sql.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw new InjectedCommandFailure();
                }
            });
        }
    }
    public sealed class InjectedCommandFailure : Exception;
    private sealed class CountedConnection(MySqlConnection inner, Action command, Action<string> execute) : IDbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ConnectionString
        {
            get => inner.ConnectionString; set => inner.ConnectionString = value ?? "";
        }
        public int ConnectionTimeout => inner.ConnectionTimeout; public string Database => inner.Database; public ConnectionState State => inner.State;
        public IDbTransaction BeginTransaction() => inner.BeginTransaction(); public IDbTransaction BeginTransaction(IsolationLevel level) => inner.BeginTransaction(level);
        public void ChangeDatabase(string name) => inner.ChangeDatabase(name); public void Close() => inner.Close(); public void Open() => inner.Open(); public void Dispose() => inner.Dispose();
        public IDbCommand CreateCommand()
        {
            command();

            return new InterceptedCommand(inner.CreateCommand(), execute);
        }
    }
    private sealed class InterceptedCommand(IDbCommand inner, Action<string> execute) : IDbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string CommandText
        {
            get => inner.CommandText; set => inner.CommandText = value ?? "";
        }
        public int CommandTimeout
        {
            get => inner.CommandTimeout; set => inner.CommandTimeout = value;
        }
        public CommandType CommandType
        {
            get => inner.CommandType; set => inner.CommandType = value;
        }
        public IDbConnection? Connection
        {
            get => inner.Connection; set => inner.Connection = value;
        }
        public IDataParameterCollection Parameters => inner.Parameters;
        public IDbTransaction? Transaction
        {
            get => inner.Transaction; set => inner.Transaction = value;
        }
        public UpdateRowSource UpdatedRowSource
        {
            get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value;
        }
        public void Cancel() => inner.Cancel(); public IDbDataParameter CreateParameter() => inner.CreateParameter(); public void Dispose() => inner.Dispose();
        public void Prepare() => inner.Prepare();
        public int ExecuteNonQuery()
        {
            execute(CommandText);

            return inner.ExecuteNonQuery();
        }
        public object? ExecuteScalar()
        {
            execute(CommandText);

            return inner.ExecuteScalar();
        }
        public IDataReader ExecuteReader()
        {
            execute(CommandText);

            return inner.ExecuteReader();
        }
        public IDataReader ExecuteReader(CommandBehavior behavior)
        {
            execute(CommandText);

            return inner.ExecuteReader(behavior);
        }
    }
}
