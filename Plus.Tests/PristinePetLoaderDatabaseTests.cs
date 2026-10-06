using System.Data;
using System.Reflection;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Users.Inventory.Pets;
using Xunit;

namespace Plus.Tests;

public sealed class StagedLoaderDatabaseFactAttribute : FactAttribute
{
    public StagedLoaderDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("STAGED_LOADER_DATABASE") is null) {
            Skip = "Opt-in isolated pristine loader MariaDB probe.";
        }
    }
}

[CollectionDefinition("Pristine staged loaders", DisableParallelization = true)]
public sealed class PristineStagedLoaderCollection;

[Collection("Pristine staged loaders")]
public sealed class PristinePetLoaderDatabaseTests
{
    [StagedLoaderDatabaseFact]
    public void InventoryPetsLoadFromPristineColumnsWithoutNarrowingIdsOrLosingNullStrings()
    {
        PristineStagedDatabase.Run(["users", "bots", "bots_petdata"], (database, connection) =>
        {
            var loader = new PetLoader(database);
            Assert.Empty(loader.GetPetsForUser(7));
            connection.Execute("INSERT INTO users(id,username,auth_ticket) VALUES (7,'owner','ticket'); ALTER TABLE bots_petdata MODIFY createstamp INT NULL");
            connection.Execute("""
                INSERT INTO bots (id, user_id, room_id, ai_type, name, motto, look, x, y, z) VALUES
                    (10, 7, 0, 'pet', 'pet', '', '', 1, 2, 3),
                    (11, 7, 0, 'generic', 'bot', '', '', 0, 0, 0),
                    (12, 8, 0, 'pet', 'other owner', '', '', 0, 0, 0),
                    (13, 7, 42, 'pet', 'placed', '', '', 0, 0, 0),
                    (14, 7, 0, 'pet', 'missing data', '', '', 0, 0, 0),
                    (15, 7, 0, 'pet', 'nullable strings', '', '', 0, 0, 0);
                INSERT INTO bots_petdata (id, type, race, color, experience, energy, nutrition, respect, createstamp,
                                          have_saddle, anyone_ride, hairdye, pethair, gnome_clothing) VALUES
                    (10, 2, '3', 'ffffff', 4, 5, 6, 7, 1487474034, 1, 0, 9, 10, 'hat'),
                    (15, 3, NULL, NULL, 0, 0, 0, 0, 1487474000, 0, 1, 1, -1, NULL);
                INSERT INTO bots_petdata (id) VALUES (11), (12), (13);
                """);
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/20_UseUtcPetCreationTime.sql")));

            var pets = loader.GetPetsForUser(7);
            Assert.Equal([10, 15], pets.Select(pet => pet.PetId).OrderBy(id => id));
            var pet = Assert.Single(pets, row => row.PetId == 10);
            Assert.Equal((10, 7, 0u, "pet", 2, "3", "ffffff"), (pet.PetId, pet.OwnerId, pet.RoomId, pet.Name, pet.Type, pet.Race, pet.Color));
            Assert.Equal((4, 5, 6, 7, (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(1487474034)), (pet.Experience, pet.Energy, pet.Nutrition, pet.Respect, pet.CreatedAt));
            Assert.Equal((1, 2, 3d, 1, 0, 9, 10, "hat"), (pet.X, pet.Y, pet.Z, pet.Saddle, pet.AnyoneCanRide, pet.HairDye, pet.PetHair, pet.GnomeClothing));
            Assert.Equal("owner", pet.OwnerName);
            var nullable = Assert.Single(pets, row => row.PetId == 15);
            Assert.Equal(("", "", ""), (nullable.Race, nullable.Color, nullable.GnomeClothing));
            Assert.Empty(loader.GetPetsForUser(9));
            connection.Execute("DELETE FROM users WHERE id=7");
            Assert.All(loader.GetPetsForUser(7), item => Assert.Equal("", item.OwnerName));

            connection.Execute("UPDATE bots_petdata SET type = @type WHERE id = 10", new { type = 2147483648u });
            Assert.Throws<OverflowException>(() => loader.GetPetsForUser(7));
            connection.Execute("UPDATE bots_petdata SET type = 2 WHERE id = 10");
            connection.Execute("UPDATE bots SET id = @id, user_id = @id WHERE id = 10; UPDATE bots_petdata SET id = @id WHERE id = 10", new { id = int.MaxValue });
            var largest = Assert.Single(loader.GetPetsForUser(int.MaxValue));
            Assert.Equal((int.MaxValue, int.MaxValue), (largest.PetId, largest.OwnerId));
            connection.Execute("UPDATE bots SET id = @overflow WHERE id = @id; UPDATE bots_petdata SET id = @overflow WHERE id = @id",
                new { overflow = 2147483648u, id = int.MaxValue });
            Assert.Throws<OverflowException>(() => loader.GetPetsForUser(int.MaxValue));
        });
    }
}

internal static class PristineStagedDatabase
{
    internal static void Run(string[] tables, Action<IDatabase, MySqlConnection> test)
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("STAGED_LOADER_DATABASE"))
        {
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var connection = new MySqlConnection(options.ConnectionString);
        connection.Open();
        var schema = "staged_loaders_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");

        try {
            connection.Execute($"USE `{schema}`");
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));

            foreach (var table in tables) {
                var start = pristine.IndexOf($"CREATE TABLE `{table}` (", StringComparison.Ordinal);
                Assert.True(start >= 0, $"Missing pristine CREATE TABLE for {table}.");
                var end = pristine.IndexOf(';', start);
                Assert.True(end >= 0, $"Unterminated pristine CREATE TABLE for {table}.");
                connection.Execute(pristine[start..(end + 1)]);
            }

            options.Database = schema;
            test(new ProbeDatabase(options.ConnectionString), connection);
        }
        finally {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    internal static T Proxy<T>(Func<string, object?[], object?> callback) where T : class
    {
        var proxy = DispatchProxy.Create<T, StagedLoaderCallbackProxy>();
        ((StagedLoaderCallbackProxy)(object)proxy).Callback = callback;

        return proxy;
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}

public class StagedLoaderCallbackProxy : DispatchProxy
{
    public Func<string, object?[], object?> Callback { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Callback(method!.Name, args ?? []);
}
