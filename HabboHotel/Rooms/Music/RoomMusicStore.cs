using System.Collections.Immutable;
using System.Data;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Rooms.Music
{
    public interface IRoomMusicStore
    {
        IReadOnlyList<MusicSong> Songs(IReadOnlyList<int> ids);
        int OfficialSong(string code);
        IReadOnlyList<MusicTrack> Inventory(int ownerId, IReadOnlyList<uint> ids);
        MusicPlayerState Load(uint playerId);
        InventoryItem? AvailableDisc(uint discId, uint ownerId);
        bool ReturnInvalidPlayer(uint playerId, uint roomId, uint ownerId);
        MusicPlayerState? Add(uint playerId, uint roomId, int ownerId, long version, uint discId, uint baseItem,
            int position, int capacity, int startIndex, DateTimeOffset? startedAt);
        (MusicPlayerState State, InventoryItem Disc)? Remove(uint playerId, uint roomId, int ownerId, long version,
            int position, int startIndex, DateTimeOffset? startedAt);
        long? Playback(uint playerId, uint roomId, uint ownerId, long version, int startIndex, DateTimeOffset? startedAt);
    }

    public sealed class RoomMusicStore(IDatabase database, IItemDataManager definitions) : IRoomMusicStore
    {
        private const string SongColumns = "song.id,song.code,song.name,song.creator,song.trax_data AS Data,song.length_ms AS LengthMs";

        public IReadOnlyList<MusicSong> Songs(IReadOnlyList<int> ids)
        {
            if (ids.Count == 0) {
                return [];
            }

            using var connection = database.Connection();

            return connection.Query<SongRow>($"SELECT {SongColumns} FROM room_music_songs song WHERE song.id IN @ids", new { ids })
                .Select(row => row.Snapshot()).Where(song => song.IsValid).ToArray();
        }

        public int OfficialSong(string code)
        {
            using var connection = database.Connection();
            var rows = connection.Query<SongRow>($"SELECT {SongColumns} FROM room_music_songs song WHERE BINARY song.code=BINARY @code LIMIT 2", new { code }).ToArray();

            return rows.Length == 1 && rows[0].Snapshot().IsValid ? rows[0].Id : -1;
        }

        public IReadOnlyList<MusicTrack> Inventory(int ownerId, IReadOnlyList<uint> ids)
        {
            if (ids.Count == 0) {
                return [];
            }

            using var connection = database.Connection();
            var items = connection.Query<ItemRow>("""
                SELECT id,user_id AS OwnerId,room_id AS RoomId,base_item AS BaseItem,extra_data AS ExtraData,
                       limited_number AS LimitedNumber,limited_stack AS LimitedStack
                FROM items WHERE user_id=@ownerId AND room_id=0 AND id IN @ids
                    AND NOT EXISTS (SELECT 1 FROM room_music_playlist link WHERE link.disc_id=items.id)
                """, new { ownerId, ids }).ToArray();
            var defaults = connection.Query<DiscDefinitionRow>("SELECT base_item AS BaseItem,song_id AS SongId FROM room_music_disc_definitions WHERE base_item IN @bases",
                new { bases = items.Select(item => item.BaseItem).Distinct().ToArray() }).ToDictionary(row => row.BaseItem, row => row.SongId);
            var songs = Songs(items.Select(item => SongId(item, defaults)).Where(id => id > 0).Distinct().ToArray()).ToDictionary(song => song.Id);
            var map = definitions.Items;

            return items.Where(item => map.TryGetValue(item.BaseItem, out var definition) && RoomMusicDefinition.IsDisc(definition))
                .Select(item => songs.TryGetValue(SongId(item, defaults), out var song) ? new MusicTrack(item.Id, item.OwnerId, song) : null)
                .Where(track => track != null).Select(track => track!).ToArray();
        }

        public InventoryItem? AvailableDisc(uint discId, uint ownerId)
        {
            using var connection = database.Connection();

            return ReadAvailableDisc(connection, definitions, discId, ownerId);
        }

        internal static InventoryItem? ReadAvailableDisc(IDbConnection connection, IItemDataManager definitions, uint discId, uint ownerId)
        {
            var row = connection.QuerySingleOrDefault<ItemRow>("""
                SELECT id,user_id AS OwnerId,base_item AS BaseItem,extra_data AS ExtraData,
                       limited_number AS LimitedNumber,limited_stack AS LimitedStack
                FROM items WHERE id=@discId AND user_id=@ownerId AND room_id=0
                    AND NOT EXISTS (SELECT 1 FROM room_music_playlist link WHERE link.disc_id=items.id)
                """, new { discId, ownerId });
            var map = definitions.Items;

            return row != null && map.TryGetValue(row.BaseItem, out var definition) && RoomMusicDefinition.IsDisc(definition)
                ? RoomMusicDefinition.Inventory(row.Id, row.OwnerId, definition, row.ExtraData, row.LimitedNumber, row.LimitedStack) : null;
        }

        public MusicPlayerState Load(uint playerId)
        {
            using var connection = database.Connection();

            return State(connection, null, playerId);
        }

        public bool ReturnInvalidPlayer(uint playerId, uint roomId, uint ownerId)
        {
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (connection.Execute("UPDATE items SET room_id=0,extra_data='0' WHERE id=@playerId AND room_id=@roomId AND user_id=@ownerId",
                new { playerId, roomId, ownerId }, transaction) != 1) {
                return false;
            }

            connection.Execute("UPDATE room_music_players SET started_at=NULL,version=version+1 WHERE item_id=@playerId", new { playerId }, transaction);
            transaction.Commit();

            return true;
        }

        public MusicPlayerState? Add(uint playerId, uint roomId, int ownerId, long version, uint discId, uint baseItem,
            int position, int capacity, int startIndex, DateTimeOffset? startedAt)
        {
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (!LockPlayer(connection, transaction, playerId, roomId, ownerId, version)) {
                return null;
            }

            var count = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_music_playlist WHERE player_id=@playerId", new { playerId }, transaction);

            if (position < 0 || position > count || count >= capacity) {
                return null;
            }

            var disc = connection.QuerySingleOrDefault<ItemRow>("""
                SELECT id,user_id AS OwnerId,room_id AS RoomId,base_item AS BaseItem,extra_data AS ExtraData,
                       limited_number AS LimitedNumber,limited_stack AS LimitedStack
                FROM items WHERE id=@discId AND user_id=@ownerId AND room_id=0 AND base_item=@baseItem FOR UPDATE
                """, new { discId, ownerId, baseItem }, transaction);
            var map = definitions.Items;

            if (disc == null || !map.TryGetValue(baseItem, out var definition) || !RoomMusicDefinition.IsDisc(definition)
                || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_music_playlist WHERE disc_id=@discId", new { discId }, transaction) != 0) {
                return null;
            }

            var songId = RoomMusicDefinition.SongId(disc.ExtraData);

            if (songId == 0) {
                songId = connection.QuerySingleOrDefault<int>("SELECT song_id FROM room_music_disc_definitions WHERE base_item=@baseItem LOCK IN SHARE MODE", new { baseItem }, transaction);
            }

            var song = connection.QuerySingleOrDefault<SongRow>($"SELECT {SongColumns} FROM room_music_songs song WHERE song.id=@songId LOCK IN SHARE MODE", new { songId }, transaction);

            if (song == null || !song.Snapshot().IsValid) {
                return null;
            }

            connection.Execute("UPDATE room_music_playlist SET position=position+1 WHERE player_id=@playerId AND position>=@position ORDER BY position DESC", new { playerId, position }, transaction);
            connection.Execute("INSERT INTO room_music_playlist (player_id,disc_id,position,song_id) VALUES (@playerId,@discId,@position,@songId)", new { playerId, discId, position, songId }, transaction);
            SavePlayback(connection, transaction, playerId, startIndex, startedAt);
            var result = State(connection, transaction, playerId);

            if (!result.Usable) {
                return null;
            }

            transaction.Commit();

            return result;
        }

        public (MusicPlayerState State, InventoryItem Disc)? Remove(uint playerId, uint roomId, int ownerId, long version,
            int position, int startIndex, DateTimeOffset? startedAt)
        {
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (!LockPlayer(connection, transaction, playerId, roomId, ownerId, version)) {
                return null;
            }

            var disc = connection.QuerySingleOrDefault<ItemRow>("""
                SELECT item.id,item.user_id AS OwnerId,item.room_id AS RoomId,item.base_item AS BaseItem,item.extra_data AS ExtraData,
                       item.limited_number AS LimitedNumber,item.limited_stack AS LimitedStack
                FROM room_music_playlist link JOIN items item ON item.id=link.disc_id
                WHERE link.player_id=@playerId AND link.position=@position FOR UPDATE
                """, new { playerId, position }, transaction);
            var map = definitions.Items;

            if (disc == null || disc.RoomId != 0 || !map.TryGetValue(disc.BaseItem, out var definition) || !RoomMusicDefinition.IsDisc(definition)) {
                return null;
            }

            connection.Execute("DELETE FROM room_music_playlist WHERE player_id=@playerId AND position=@position", new { playerId, position }, transaction);
            connection.Execute("UPDATE room_music_playlist SET position=position-1 WHERE player_id=@playerId AND position>@position ORDER BY position", new { playerId, position }, transaction);
            SavePlayback(connection, transaction, playerId, startIndex, startedAt);
            var result = State(connection, transaction, playerId);

            if (!result.Usable) {
                return null;
            }

            transaction.Commit();

            return (result, RoomMusicDefinition.Inventory(disc.Id, disc.OwnerId, definition, disc.ExtraData, disc.LimitedNumber, disc.LimitedStack));
        }

        public long? Playback(uint playerId, uint roomId, uint ownerId, long version, int startIndex, DateTimeOffset? startedAt)
        {
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (ownerId is 0 or > int.MaxValue || !LockPlayer(connection, transaction, playerId, roomId, (int)ownerId, version)) {
                return null;
            }

            var state = State(connection, transaction, playerId);

            if (!state.Usable || state.Tracks.Length == 0 || startIndex < 0 || startIndex >= state.Tracks.Length) {
                return null;
            }

            SavePlayback(connection, transaction, playerId, startIndex, startedAt);
            transaction.Commit();

            return checked(version + 1);
        }

        private static bool LockPlayer(IDbConnection connection, IDbTransaction transaction, uint playerId, uint roomId, int ownerId, long version)
        {
            if (connection.QuerySingleOrDefault<int?>("SELECT id FROM users WHERE id=@ownerId FOR UPDATE", new { ownerId }, transaction) == null) {
                return false;
            }

            if (connection.QuerySingleOrDefault<uint?>("SELECT id FROM items WHERE id=@playerId AND user_id=@ownerId AND room_id=@roomId FOR UPDATE",
                new { playerId, ownerId, roomId }, transaction) == null) {
                return false;
            }

            connection.Execute("INSERT IGNORE INTO room_music_players (item_id) VALUES (@playerId)", new { playerId }, transaction);

            return connection.QuerySingle<long>("SELECT version FROM room_music_players WHERE item_id=@playerId FOR UPDATE", new { playerId }, transaction) == version;
        }

        private static void SavePlayback(IDbConnection connection, IDbTransaction transaction, uint playerId, int startIndex, DateTimeOffset? startedAt)
        {
            connection.Execute("UPDATE room_music_players SET start_index=@startIndex,started_at=@startedAt,version=version+1 WHERE item_id=@playerId",
                new { playerId, startIndex, startedAt = startedAt?.UtcDateTime }, transaction);
            connection.Execute("UPDATE items SET extra_data=@state WHERE id=@playerId", new { playerId, state = startedAt == null ? "0" : "1" }, transaction);
        }

        private MusicPlayerState State(IDbConnection connection, IDbTransaction? transaction, uint playerId)
        {
            var player = connection.QuerySingleOrDefault<PlayerRow>("SELECT start_index AS StartIndex,started_at AS StartedAt,version FROM room_music_players WHERE item_id=@playerId", new { playerId }, transaction);

            if (player == null) {
                return new([], 0, null, 0, true);
            }

            var tracks = connection.Query<TrackRow>($"""
                SELECT link.disc_id AS DiscId,item.user_id AS OwnerId,link.position,item.room_id AS RoomId,item.base_item AS BaseItem,{SongColumns}
                FROM room_music_playlist link JOIN items item ON item.id=link.disc_id
                JOIN room_music_songs song ON song.id=link.song_id
                WHERE link.player_id=@playerId ORDER BY link.position
                """, new { playerId }, transaction).ToArray();
            var count = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_music_playlist WHERE player_id=@playerId", new { playerId }, transaction);
            var map = definitions.Items;
            var valid = count == tracks.Length && tracks.Length <= 20 && tracks.Select((track, index) => track.Position == index && track.RoomId == 0 && track.Snapshot().IsValid
                && map.TryGetValue(track.BaseItem, out var definition) && RoomMusicDefinition.IsDisc(definition)).All(value => value)
                && (tracks.Length == 0 || player.StartIndex >= 0 && player.StartIndex < tracks.Length);

            return new(valid ? tracks.Select(track => new MusicTrack(track.DiscId, track.OwnerId, track.Snapshot())).ToImmutableArray() : [],
                player.StartIndex, player.StartedAt, player.Version, valid);
        }

        private static int SongId(ItemRow item, IReadOnlyDictionary<uint, int> defaults)
            => RoomMusicDefinition.SongId(item.ExtraData) is > 0 and var id ? id : defaults.GetValueOrDefault(item.BaseItem);

        private class SongRow
        {
            public int Id { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string Creator { get; set; } = "";
            public string Data { get; set; } = "";
            public int LengthMs { get; set; }
            public MusicSong Snapshot() => new(Id, Code, Name, Creator, Data, LengthMs);
        }
        private sealed class TrackRow : SongRow
        {
            public uint DiscId { get; set; }
            public uint OwnerId { get; set; }
            public uint RoomId { get; set; }
            public int Position { get; set; }
            public uint BaseItem { get; set; }
        }
        private sealed class PlayerRow
        {
            public int StartIndex { get; set; }
            public DateTimeOffset? StartedAt { get; set; }
            public long Version { get; set; }
        }
        private sealed class DiscDefinitionRow
        {
            public uint BaseItem { get; set; }
            public int SongId { get; set; }
        }
        private sealed class ItemRow
        {
            public uint Id { get; set; }
            public uint OwnerId { get; set; }
            public uint RoomId { get; set; }
            public uint BaseItem { get; set; }
            public string ExtraData { get; set; } = "";
            public uint LimitedNumber { get; set; }
            public uint LimitedStack { get; set; }
        }
    }
}
