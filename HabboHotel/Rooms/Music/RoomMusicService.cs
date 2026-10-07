using System.Collections.Immutable;
using System.Text;
using Plus.Communication.Packets.Outgoing.Sound;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Music
{
    public interface IRoomMusicService
    {
        void Songs(GameClient session, IReadOnlyList<int> ids);
        void OfficialSong(GameClient session, string code);
        void Inventory(GameClient session);
    }

    public sealed class RoomMusicService(IRoomMusicStore store) : IRoomMusicService
    {
        public void Songs(GameClient session, IReadOnlyList<int> ids)
        {
            var habbo = session.GetHabbo();

            if (!Current(session, habbo) || ids.Count > 100) {
                return;
            }

            var bytes = 0;
            var songs = store.Songs(ids.Where(id => id > 0).Distinct().ToArray()).Where(song =>
            {
                bytes += Encoding.UTF8.GetByteCount(song.Code + song.Name + song.Creator + song.Data) + 20;

                return bytes <= 450000;
            }).ToImmutableArray();

            if (Current(session, habbo)) {
                session.Send(new TraxSongInfoComposer(songs));
            }
        }
        public void OfficialSong(GameClient session, string code)
        {
            var habbo = session.GetHabbo();

            if (!Current(session, habbo) || code.Length is 0 or > 255) {
                return;
            }

            var id = store.OfficialSong(code);

            if (Current(session, habbo)) {
                session.Send(new OfficialSongIdComposer(code, id));
            }
        }
        public void Inventory(GameClient session)
        {
            var habbo = session.GetHabbo();

            if (!Current(session, habbo)) {
                return;
            }

            ImmutableArray<MusicTrack> tracks;

            lock (habbo.WalletSync) {
                if (!Current(session, habbo)) {
                    return;
                }

                var ids = habbo.Inventory.Furniture.GetItems.Where(item => RoomMusicDefinition.IsDisc(item.Definition))
                    .Take(5000).Select(item => item.Id).ToArray();
                tracks = store.Inventory(habbo.Id, ids).ToImmutableArray();
            }

            if (Current(session, habbo)) {
                session.Send(new SongDisksInventoryComposer(tracks));
            }
        }
        private static bool Current(GameClient session, Habbo? habbo) => habbo is { AccessClosed: false }
            && ReferenceEquals(session.GetHabbo(), habbo) && ReferenceEquals(habbo.Client, session);
    }
}
