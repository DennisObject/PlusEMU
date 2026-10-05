using System.Data;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Users.Clothing;
using Plus.HabboHotel.Users.Inventory.Bots;
using Xunit;

namespace Plus.Tests;

public sealed class LoginLoaderDatabaseFactAttribute : FactAttribute
{
    public LoginLoaderDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LOGIN_LOADER_DATABASE") is null)
            Skip = "Opt-in isolated login loader MariaDB probe.";
    }
}

public sealed class LoginLoaderPristineDatabaseTests
{
    [LoginLoaderDatabaseFact]
    public void PristineInventoryBotsAndClothingMaterializeWithTheirDomainTypes()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LOGIN_LOADER_DATABASE"))
        {
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var connection = new MySqlConnection(options.ConnectionString);
        connection.Open();
        var schema = "login_loaders_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            connection.Execute($"USE `{schema}`");
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            connection.Execute(CreateTable(pristine, "bots"));
            connection.Execute(CreateTable(pristine, "user_clothing"));
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(options.ConnectionString) { Database = schema }.ConnectionString);
            var bots = new BotLoader(database);
            var clothing = new ClothingStore(database);

            Assert.Multiple(
                () => Assert.Empty(bots.GetBotsForUser(7)),
                () => Assert.Empty(clothing.Load(7)));

            connection.Execute("""
                INSERT INTO bots (id, user_id, room_id, ai_type, name, motto, look, gender) VALUES
                    (10, 7, 0, 'generic', 'guide', 'hello', 'hd-180-1', 'F'),
                    (11, 8, 0, 'generic', 'other owner', '', '', 'M'),
                    (12, 7, 42, 'generic', 'placed', '', '', 'M'),
                    (13, 7, 0, 'pet', 'pet', '', '', 'M'),
                    (14, 7, 0, 'bartender', 'bartender', '', '', 'M');
                INSERT INTO user_clothing (id, user_id, part_id, part) VALUES
                    (20, 7, '1234', 'hr'), (21, 8, '5678', 'ch');
                """);

            Assert.Collection(bots.GetBotsForUser(7).OrderBy(bot => bot.Id),
                bot => Assert.Equal((10, 7, "guide", "hello", "hd-180-1", "F"),
                    (bot.Id, bot.OwnerId, bot.Name, bot.Motto, bot.Figure, bot.Gender)),
                bot => Assert.Equal((14, 7, "bartender", "", "", "M"),
                    (bot.Id, bot.OwnerId, bot.Name, bot.Motto, bot.Figure, bot.Gender)));
            var part = Assert.Single(clothing.Load(7));
            Assert.Equal((20, 1234, "hr"), (part.Id, part.PartId, part.Part));
            Assert.Empty(bots.GetBotsForUser(9));
            Assert.Empty(clothing.Load(9));

            var addedId = clothing.Add(7, int.MaxValue, "ch");
            var added = Assert.Single(clothing.Load(7), row => row.Id == addedId);
            Assert.Equal((int.MaxValue, "ch"), (added.PartId, added.Part));
            connection.Execute("UPDATE bots SET id = @id, user_id = @id WHERE id = 10", new { id = int.MaxValue });
            var largest = Assert.Single(bots.GetBotsForUser(int.MaxValue));
            Assert.Equal((int.MaxValue, int.MaxValue), (largest.Id, largest.OwnerId));
            connection.Execute("UPDATE bots SET id = @overflow WHERE id = @id", new { overflow = (uint)int.MaxValue + 1, id = int.MaxValue });
            Assert.Throws<OverflowException>(() => bots.GetBotsForUser(int.MaxValue));
            connection.Execute("UPDATE user_clothing SET part_id = '2147483648' WHERE id = 20");
            Assert.IsType<OverflowException>(Assert.Throws<DataException>(() => clothing.Load(7)).InnerException);
            connection.Execute("UPDATE user_clothing SET part_id = 'not-a-number' WHERE id = 20");
            Assert.IsType<FormatException>(Assert.Throws<DataException>(() => clothing.Load(7)).InnerException);
        }
        finally
        {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static string CreateTable(string pristine, string table)
    {
        var start = pristine.IndexOf($"CREATE TABLE `{table}` (", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing pristine CREATE TABLE for {table}.");
        var end = pristine.IndexOf(';', start);
        Assert.True(end >= 0, $"Unterminated pristine CREATE TABLE for {table}.");
        return pristine[start..(end + 1)];
    }

    private sealed class ProbeDatabase(string connectionString) : Plus.Database.IDatabase
    {
        public bool IsConnected() => true;
        [Obsolete] public Plus.Database.Interfaces.IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
