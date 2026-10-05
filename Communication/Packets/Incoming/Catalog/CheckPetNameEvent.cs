using Plus.HabboHotel.Catalog.Pets;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class CheckPetNameEvent(IPetNameValidationService names) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var name = packet.ReadString();
        names.Check(session, name);
        return Task.CompletedTask;
    }
}