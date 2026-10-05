using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class LetUserInEvent(IRoomModerationService moderation) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        moderation.AnswerDoor(room, session, packet.ReadString(), packet.ReadBool());
        return Task.CompletedTask;
    }
}