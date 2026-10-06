using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets;

internal sealed class GetPetInformationEvent(IPetInformationService pets) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        pets.SendInformation(session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
