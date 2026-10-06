using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.HabboHotel.Moderation;

namespace Plus.HabboHotel.GameClients;

public interface IClientIdentityService
{
    void SetMachineIdentity(GameClient session, string machineId);
}

public sealed class ClientIdentityService(IModerationManager moderation) : IClientIdentityService
{
    public void SetMachineIdentity(GameClient session, string machineId)
    {
        session.MachineId = machineId;

        if (moderation.HasMachineBanCheck(machineId))
        {
            session.Disconnect();

            return;
        }

        session.Send(new SetUniqueIdComposer(machineId));
    }
}
