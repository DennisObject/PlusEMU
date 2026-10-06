using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal sealed class GetRoomSettingsEvent(IRoomSettingsService settings) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        settings.Show(session, packet.ReadUInt());
        return Task.CompletedTask;
    }
}
