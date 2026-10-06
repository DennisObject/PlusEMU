using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomLoaderPristineDatabaseTests
{
    [FoundationSchemaDatabaseFact]
    public void PristineBotAndPetRowsMaterializeWithTheirDomainTypes()
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"))
        {
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var connection = new MySqlConnection(options.ConnectionString);
        connection.Open();
        var schema = "room_loaders_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");

        try
        {
            connection.Execute($"USE `{schema}`");
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            connection.Execute(CreateTable(pristine, "users"));
            connection.Execute("INSERT INTO users(id,username,auth_ticket) VALUES (9,'owner','ticket')");
            connection.Execute(CreateTable(pristine, "bots"));
            connection.Execute(CreateTable(pristine, "bots_petdata"));

            Assert.Multiple(
                () => Assert.Empty(RoomBotsComponent.Load(connection, 42)),
                () => Assert.Empty(RoomPetsComponent.Load(connection, 42)),
                () => Assert.Null(RoomPetsComponent.LoadData(connection, 12)));

            connection.Execute("ALTER TABLE bots_petdata MODIFY createstamp INT NULL");
            connection.Execute("""
                INSERT INTO bots (id, user_id, room_id, name, motto, look, x, y, z, rotation,
                                  ai_type, walk_mode, automatic_chat, speaking_interval, mix_sentences, chat_bubble) VALUES
                    (10, 7, 42, 'guide', 'hello', 'hd-180-1', 1, 2, 3, 4, 'generic', 'freeroam', 'true', 12, TRUE, 5),
                    (11, 8, 42, 'bartender', '', '', 2, 3, -1, 0, 'bartender', 'stand', 'false', 15, FALSE, 6),
                    (12, 9, 42, 'pet', '', '', 4, 5, 2, 0, 'pet', 'freeroam', 'false', 0, FALSE, 0),
                    (13, 7, 99, 'other bot', '', '', 0, 0, 0, 0, 'generic', 'freeroam', 'false', 0, FALSE, 0),
                    (14, 9, 99, 'other pet', '', '', 0, 0, 0, 0, 'pet', 'freeroam', 'false', 0, FALSE, 0);
                INSERT INTO bots_petdata (id, type, race, color, experience, energy, nutrition, respect, createstamp,
                                          have_saddle, anyone_ride, hairdye, pethair, gnome_clothing) VALUES
                    (12, 2, '3', 'ffffff', 4, 5, 6, 7, 1487474034, 1, 0, 9, 10, 'hat'),
                    (14, 3, NULL, NULL, 0, 0, 0, 0, 1487474000, 0, 1, 1, -1, NULL);
                """);
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/20_UseUtcPetCreationTime.sql")));

            Assert.Collection(RoomBotsComponent.Load(connection, 42).OrderBy(bot => bot.Id),
                bot =>
                {
                    Assert.Equal((10, 42u, 7, "generic", "freeroam"), (bot.Id, bot.RoomId, bot.UserId, bot.AiType, bot.WalkMode));
                    Assert.Equal(("guide", "hello", "hd-180-1"), (bot.Name, bot.Motto, bot.Look));
                    Assert.Equal((1, 2, 3d, 4, 12, 5), (bot.X, bot.Y, bot.Z, bot.Rotation, bot.SpeakingInterval, bot.ChatBubble));
                    Assert.True(bot.AutomaticChat);
                    Assert.True(bot.MixSentences);
                },
                bot =>
                {
                    Assert.Equal((11, 42u, 8, "bartender", "stand"), (bot.Id, bot.RoomId, bot.UserId, bot.AiType, bot.WalkMode));
                    Assert.Equal(-1d, bot.Z);
                    Assert.False(bot.AutomaticChat);
                    Assert.False(bot.MixSentences);
                });
            var pet = Assert.Single(RoomPetsComponent.Load(connection, 42));
            Assert.Equal((12, 9, 42u, "pet", 4, 5, 2d), (pet.Id, pet.UserId, pet.RoomId, pet.Name, pet.X, pet.Y, pet.Z));
            Assert.Equal("owner", pet.OwnerName);
            connection.Execute("DELETE FROM users WHERE id=9");
            Assert.Equal("", Assert.Single(RoomPetsComponent.Load(connection, 42)).OwnerName);
            var data = Assert.IsType<RoomPetsComponent.PetData>(RoomPetsComponent.LoadData(connection, pet.Id));
            Assert.Equal((2, "3", "ffffff", 4, 5, 6, 7), (data.Type, data.Race, data.Color, data.Experience, data.Energy, data.Nutrition, data.Respect));
            Assert.Equal(((DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(1487474034), 1, 0, 9, 10, "hat"), (data.CreatedAt, data.HaveSaddle, data.AnyoneRide, data.Hairdye, data.Pethair, data.GnomeClothing));
            var nullableData = Assert.IsType<RoomPetsComponent.PetData>(RoomPetsComponent.LoadData(connection, 14));
            Assert.Equal(("", "", ""), (nullableData.Race, nullableData.Color, nullableData.GnomeClothing));
            Assert.Null(RoomPetsComponent.LoadData(connection, 15));
            Assert.Empty(RoomBotsComponent.Load(connection, 100));
            Assert.Empty(RoomPetsComponent.Load(connection, 100));

            connection.Execute("UPDATE bots SET room_id = @roomId WHERE id IN (10, 12)", new
            {
                roomId = uint.MaxValue
            });
            Assert.Equal(uint.MaxValue, Assert.Single(RoomBotsComponent.Load(connection, uint.MaxValue)).RoomId);
            Assert.Equal(uint.MaxValue, Assert.Single(RoomPetsComponent.Load(connection, uint.MaxValue)).RoomId);

            connection.Execute("UPDATE bots SET id = @id WHERE id = 10", new
            {
                id = (uint)int.MaxValue + 1
            });
            AssertOverflow(() => RoomBotsComponent.Load(connection, uint.MaxValue).ToArray());
            connection.Execute("UPDATE bots SET user_id = @id WHERE id = 11", new
            {
                id = (uint)int.MaxValue + 1
            });
            AssertOverflow(() => RoomBotsComponent.Load(connection, 42).ToArray());
            connection.Execute("UPDATE bots SET user_id = @id WHERE id = 12", new
            {
                id = (uint)int.MaxValue + 1
            });
            AssertOverflow(() => RoomPetsComponent.Load(connection, uint.MaxValue).ToArray());
            connection.Execute("UPDATE bots SET id = @id WHERE id = 14", new
            {
                id = uint.MaxValue
            });
            AssertOverflow(() => RoomPetsComponent.Load(connection, 99).ToArray());
            connection.Execute("UPDATE bots_petdata SET type = @type WHERE id = 12", new
            {
                type = (uint)int.MaxValue + 1
            });
            AssertOverflow(() => RoomPetsComponent.LoadData(connection, 12));
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

    private static void AssertOverflow(Action load) => Assert.Throws<OverflowException>(load);
}
