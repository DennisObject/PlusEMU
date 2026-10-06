using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets;

internal sealed class GetPetTrainingPanelEvent(IPetInformationService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var petId = packet.ReadInt();
        service.SendTrainingPanel(session, petId);

        return Task.CompletedTask;
    }
}
