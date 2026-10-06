using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class UpdateGroupSettingsEvent(IGroupSettingsService settings) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => settings.Update(session, new(
        packet.ReadInt(), packet.ReadInt(), packet.ReadInt(), packet.ReadBool()));
}
