using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Music;

namespace Plus.Communication.Packets.Outgoing.Sound
{
    public sealed class NowPlayingComposer(MusicPlayback snapshot) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.NowPlayingComposer;
        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteInteger(snapshot.SongId);
            packet.WriteInteger(snapshot.Position);
            packet.WriteInteger(snapshot.NextSongId);
            packet.WriteInteger(snapshot.NextPosition);
            packet.WriteInteger(snapshot.ElapsedMs);
        }
    }
    public sealed class JukeboxPlaylistComposer(MusicPlaylist snapshot) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.JukeboxPlaylistComposer;
        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteInteger(snapshot.Capacity);
            packet.WriteInteger(snapshot.Tracks.Length);

            foreach (var track in snapshot.Tracks) {
                packet.WriteUInteger(track.DiscId);
                packet.WriteInteger(track.Song.Id);
            }
        }
    }
    public sealed class JukeboxPlaylistFullComposer : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.JukeboxPlaylistFullComposer;
        public void Compose(IOutgoingPacket packet)
        {
        }
    }
    public sealed class SongDisksInventoryComposer(ImmutableArray<MusicTrack> snapshot) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.SongDisksInventoryComposer;
        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteInteger(snapshot.Length);

            foreach (var track in snapshot) {
                packet.WriteUInteger(track.DiscId);
                packet.WriteInteger(track.Song.Id);
            }
        }
    }
    public sealed class SoundMachinePlaylistComposer(MusicPlaylist snapshot) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.SoundMachinePlaylistComposer;
        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteInteger(snapshot.ElapsedMs);
            packet.WriteInteger(snapshot.Tracks.Length);

            foreach (var track in snapshot.Tracks) {
                packet.WriteInteger(track.Song.Id);
                packet.WriteInteger(track.Song.LengthMs);
                packet.WriteString(track.Song.Name);
                packet.WriteString(track.Song.Creator);
            }
        }
    }
    public sealed class OfficialSongIdComposer(string code, int songId) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.OfficialSongIdComposer;
        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteString(code);
            packet.WriteInteger(songId);
        }
    }
}
