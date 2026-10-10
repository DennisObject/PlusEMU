using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms.Games;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class RoomHighscoreDatabaseTests
{
    [WiredChestDatabaseFact]
    public void PairedCommitRollsBackFirstBoardWhenSecondUpdateFails()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var writes = Seed(db);
        db.Connection.Execute("CREATE TRIGGER reject_second_board BEFORE UPDATE ON items FOR EACH ROW BEGIN IF NEW.id=502 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='second board failure'; END IF; END");
        var store = new RoomHighscoreStore(db.Database);
        Assert.Throws<MySqlException>(() => store.Commit(writes));
        Assert.Equal(writes.Select(write => write.Prior), Payloads(db));
        db.Connection.Execute("DROP TRIGGER reject_second_board");
        Assert.True(store.Commit(writes));
        Assert.Equal(writes.Select(write => write.Candidate), Payloads(db));
        Assert.False(store.Commit(writes)); // Never recompute a wins increment from the now-updated rows.
    }

    [WiredChestDatabaseTheory]
    [InlineData("room_id", "43")]
    [InlineData("user_id", "8")]
    [InlineData("base_item", "999")]
    [InlineData("extra_data", "'another payload'")]
    public void FrozenBatchRejectsAChangedPriorOrRowIdentity(string column, string value)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var writes = Seed(db);
        db.Connection.Execute($"UPDATE items SET {column}={value} WHERE id=502");
        Assert.False(new RoomHighscoreStore(db.Database).Commit(writes));
        Assert.Equal(writes[0].Prior, db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
    }

    [WiredChestDatabaseFact]
    public void MutableDefinitionCannotChangeTheFrozenExpectedBase()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var writes = Seed(db);
        writes[1].Definition.Id = 999;
        db.Connection.Execute("UPDATE items SET base_item=999 WHERE id=502");
        Assert.False(new RoomHighscoreStore(db.Database).Commit(writes));
        Assert.Equal(writes[0].Prior, db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
    }

    private static HighscoreWrite[] Seed(WiredChestDatabaseTests.Fixture db) => Enumerable.Range(501, 2).Select(id =>
    {
        var kind = id == 501 ? 3u : 1u;
        var item = new Item
        {
            Id = (uint)id,
            RoomId = 42,
            OwnerId = 7,
            Definition = new()
            {
                Id = (uint)id,
                Type = ItemType.Floor,
                ItemName = kind == 3 ? "highscore_fastesttime*1" : "highscore_mostwin*1"
            },
            ExtraData = new HighscoreDataFormat { State = "0", ScoreType = kind }
        };
        var prior = item.ExtraData.Serialize();
        var candidate = new HighscoreDataFormat { State = "0", ScoreType = kind, Entries = [new(kind == 3 ? 2 : 1, ["owner"])] }.Serialize();
        db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(@id,7,42,@id,@prior)", new { item.Id, prior });

        return new HighscoreWrite(item, item.Placement, item.Definition, 42, 7, prior, candidate);
    }).ToArray();

    private static string[] Payloads(WiredChestDatabaseTests.Fixture db) =>
        db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray();
}
