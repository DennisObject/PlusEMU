using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class MoveAvatarEvent(IRoomAvatarActionService actions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var x = packet.ReadInt();
        var y = packet.ReadInt();
        actions.Move(session, x, y);
        return Task.CompletedTask;
    }
}