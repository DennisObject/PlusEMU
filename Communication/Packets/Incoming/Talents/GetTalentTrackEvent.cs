using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Talents;

namespace Plus.Communication.Packets.Incoming.Talents;

internal class GetTalentTrackEvent(ITalentTrackPresentationService presentation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        presentation.ShowLevels(session, packet.ReadString());

        return Task.CompletedTask;
    }
}
