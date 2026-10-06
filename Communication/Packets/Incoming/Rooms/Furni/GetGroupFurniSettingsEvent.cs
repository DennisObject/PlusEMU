using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class GetGroupFurniSettingsEvent(IGroupPresentationService presentation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var groupId = packet.ReadInt();
        presentation.ShowFurnitureSettings(session, itemId, groupId);

        return Task.CompletedTask;
    }
}
