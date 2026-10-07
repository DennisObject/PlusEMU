using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Music;

namespace Plus.Communication.Packets.Incoming.Sound
{
    internal sealed class GetUserSongDisksEvent(IRoomMusicService music) : IPacketEvent
    {
        public Task Parse(GameClient session, IIncomingPacket packet)
        {
            music.Inventory(session);

            return Task.CompletedTask;
        }
    }
}
