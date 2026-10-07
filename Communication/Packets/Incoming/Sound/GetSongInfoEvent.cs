using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Music;

namespace Plus.Communication.Packets.Incoming.Sound;

internal sealed class GetSongInfoEvent(IRoomMusicService music) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var count = packet.ReadInt();

        if (count is < 0 or > 100) {
            return Task.CompletedTask;
        }

        var ids = new int[count];

        for (var index = 0; index < count; index++) {
            ids[index] = packet.ReadInt();
        }

        music.Songs(session, ids);

        return Task.CompletedTask;
    }
}
