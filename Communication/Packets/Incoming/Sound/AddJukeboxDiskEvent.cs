using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Sound
{
    internal sealed class AddJukeboxDiskEvent : RoomPacketEvent
    {
        public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
        {
            room.Music.Add(session, packet.ReadUInt(), packet.ReadInt());

            return Task.CompletedTask;
        }
    }
}
