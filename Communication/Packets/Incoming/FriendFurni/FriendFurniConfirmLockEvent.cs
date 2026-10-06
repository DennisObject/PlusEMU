using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.FriendFurni;

internal sealed class FriendFurniConfirmLockEvent(ILoveLockService loveLocks) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        loveLocks.Confirm(session, new(packet.ReadUInt(), packet.ReadBool()));
        return Task.CompletedTask;
    }
}
