using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomComponentDatabaseFactAttribute : FactAttribute
{
    public RoomComponentDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE") is null)
            Skip = "Opt-in isolated room component MariaDB probe.";
    }
}

public sealed class RoomComponentDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void NativeBoolBotAndPetRowsMaterializeThroughComponentQueries()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "room_component_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE bots (
                    id INT PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL, name VARCHAR(50) NOT NULL,
                    motto VARCHAR(50) NOT NULL DEFAULT '', look VARCHAR(100) NOT NULL DEFAULT '', x INT NOT NULL, y INT NOT NULL,
                    z DOUBLE NOT NULL, rotation INT NOT NULL DEFAULT 0, ai_type VARCHAR(20) NOT NULL, walk_mode VARCHAR(20) NOT NULL,
                    automatic_chat BOOL NOT NULL, speaking_interval INT NOT NULL, mix_sentences BOOL NOT NULL, chat_bubble INT NOT NULL);
                CREATE TABLE bots_speech (bot_id INT NOT NULL, text VARCHAR(255) NOT NULL);
                CREATE TABLE bots_petdata (
                    id INT PRIMARY KEY, type INT NOT NULL, race VARCHAR(20) NOT NULL, color VARCHAR(20) NOT NULL,
                    experience INT NOT NULL, energy INT NOT NULL, nutrition INT NOT NULL, respect INT NOT NULL,
                    createstamp DOUBLE NOT NULL, have_saddle INT NOT NULL, anyone_ride INT NOT NULL,
                    hairdye INT NOT NULL, pethair INT NOT NULL, gnome_clothing VARCHAR(100) NOT NULL);
                """);
            connection.Execute("""
                INSERT INTO bots VALUES
                    (10, 7, 42, 'guide', 'hello', 'hd-180-1', 1, 2, 0, 3, 'generic', 'freeroam', TRUE, 12, TRUE, 5),
                    (11, 8, 42, 'pet', '', '', 4, 5, 1.5, 0, 'pet', 'freeroam', FALSE, 0, FALSE, 0),
                    (12, 9, 99, 'other', '', '', 0, 0, 0, 0, 'generic', 'freeroam', FALSE, 1, FALSE, 0);
                INSERT INTO bots_speech VALUES (10, 'first'), (10, 'second');
                INSERT INTO bots_petdata VALUES (11, 2, '3', 'ffffff', 4, 5, 6, 7, 8.5, 1, 0, 9, 10, 'hat');
                """);

            var bot = Assert.Single(RoomBotsComponent.Load(connection, 42));
            Assert.True(bot.AutomaticChat);
            Assert.True(bot.MixSentences);
            Assert.Equal(["first", "second"], RoomBotsComponent.LoadSpeech(connection, bot.Id));
            var pet = Assert.Single(RoomPetsComponent.Load(connection, 42));
            var data = Assert.IsType<RoomPetsComponent.PetData>(RoomPetsComponent.LoadData(connection, pet.Id));
            Assert.Equal((11, 42u, 1.5), (pet.Id, pet.RoomId, pet.Z));
            Assert.Equal((2, "3", "hat"), (data.Type, data.Race, data.GnomeClothing));
        }
        finally
        {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
