using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Rooms.Chat.Logs;

public sealed class ChatlogManager : IChatlogManager
{
    private readonly IDatabase _database;
    private const int FlushOnCount = 10;

    private readonly List<ChatlogEntry> _chatlogs;
    private readonly ReaderWriterLockSlim _lock;

    public ChatlogManager(IDatabase database)
    {
        _database = database;
        _chatlogs = new();
        _lock = new(LockRecursionPolicy.NoRecursion);
    }

    public void StoreChatlog(ChatlogEntry entry)
    {
        _lock.EnterUpgradeableReadLock();
        _chatlogs.Add(entry);
        OnChatlogStore();
        _lock.ExitUpgradeableReadLock();
    }

    private void OnChatlogStore()
    {
        if (_chatlogs.Count >= FlushOnCount)
            FlushAndSave();
    }

    public void FlushAndSave()
    {
        _lock.EnterWriteLock();
        try
        {
            if (_chatlogs.Count == 0)
                return;

            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute(
                "INSERT INTO chatlogs (`user_id`, `room_id`, `timestamp`, `message`) VALUES (@PlayerId, @RoomId, @Timestamp, @Message)",
                _chatlogs, transaction);
            transaction.Commit();
            _chatlogs.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
