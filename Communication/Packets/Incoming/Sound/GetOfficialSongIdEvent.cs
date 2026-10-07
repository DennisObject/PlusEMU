using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Music;

namespace Plus.Communication.Packets.Incoming.Sound
{
    internal sealed class GetOfficialSongIdEvent(IRoomMusicService music) : IPacketEvent
    {
        public Task Parse(GameClient session, IIncomingPacket packet)
        {
            music.OfficialSong(session, packet.ReadString());

            return Task.CompletedTask;
        }
    }
}
