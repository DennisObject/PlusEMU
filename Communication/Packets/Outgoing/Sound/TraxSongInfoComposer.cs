using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Music;

namespace Plus.Communication.Packets.Outgoing.Sound;

public sealed class TraxSongInfoComposer(ImmutableArray<MusicSong> songs) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.TraxSongInfoComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(songs.Length);

        foreach (var song in songs) {
            packet.WriteInteger(song.Id);
            packet.WriteString(song.Code);
            packet.WriteString(song.Name);
            packet.WriteString(song.Data);
            packet.WriteInteger(song.LengthMs);
            packet.WriteString(song.Creator);
        }
    }
}
