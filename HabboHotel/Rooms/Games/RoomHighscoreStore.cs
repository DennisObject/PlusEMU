using System.Data;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Games;

internal sealed record HighscoreWrite(Item Item, long Placement, ItemDefinition Definition, uint RoomId, uint OwnerId,
    string Prior, string Candidate)
{
    internal string LivePrior { get; init; } = Prior;
    internal string LiveCandidate { get; init; } = Candidate;
    internal uint ItemId { get; } = Item.Id;
    internal uint BaseItem { get; } = Definition.Id;
    internal string DefinitionName { get; } = Definition.ItemName;
}
internal sealed record HighscoreRow(uint Id, uint RoomId, uint OwnerId, uint BaseItem, string ExtraData)
{
    public HighscoreRow() : this(0, 0, 0, 0, "") { }
}
internal interface IRoomHighscoreStore
{
    bool Commit(IReadOnlyList<HighscoreWrite> writes);
    IReadOnlyList<HighscoreRow> Read(IReadOnlyList<HighscoreWrite> writes);
}

internal sealed class RoomHighscoreStore(IDatabase database) : IRoomHighscoreStore
{
    public bool Commit(IReadOnlyList<HighscoreWrite> writes)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        foreach (var write in writes.OrderBy(write => write.ItemId)) {
            var row = connection.QuerySingleOrDefault<HighscoreRow>(
                "SELECT id Id,room_id RoomId,user_id OwnerId,base_item BaseItem,extra_data ExtraData FROM items WHERE id=@ItemId FOR UPDATE",
                new { write.ItemId }, transaction);

            if (!Matches(row, write) || !string.Equals(row!.ExtraData, write.Prior, StringComparison.Ordinal)) {
                return false;
            }
        }

        foreach (var write in writes) {
            if (write.Prior != write.Candidate) {
                if (connection.Execute("UPDATE items SET extra_data=@Candidate WHERE id=@ItemId AND room_id=@RoomId AND user_id=@OwnerId AND base_item=@baseItem",
                        new { write.Candidate, write.ItemId, write.RoomId, write.OwnerId, baseItem = write.BaseItem }, transaction) != 1) {
                    return false;
                }
            }
        }

        transaction.Commit();

        return true;
    }

    public IReadOnlyList<HighscoreRow> Read(IReadOnlyList<HighscoreWrite> writes)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var rows = new List<HighscoreRow>();

        foreach (var write in writes.OrderBy(write => write.ItemId)) {
            var row = connection.QuerySingleOrDefault<HighscoreRow>(
                "SELECT id Id,room_id RoomId,user_id OwnerId,base_item BaseItem,extra_data ExtraData FROM items WHERE id=@ItemId FOR UPDATE",
                new { write.ItemId }, transaction);

            if (!Matches(row, write)) {
                return [];
            }

            rows.Add(row!);
        }

        transaction.Commit();

        return rows;
    }

    private static bool Matches(HighscoreRow? row, HighscoreWrite write) => row != null && row.Id == write.ItemId
        && row.RoomId == write.RoomId && row.OwnerId == write.OwnerId && row.BaseItem == write.BaseItem;
}
