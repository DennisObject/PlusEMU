using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Handshake;

[NoAuthenticationRequired]
public class UniqueIdEvent(IClientIdentityService identity) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadString();
        var machineId = packet.ReadString();
        identity.SetMachineIdentity(session, machineId);

        return Task.CompletedTask;
    }
}
