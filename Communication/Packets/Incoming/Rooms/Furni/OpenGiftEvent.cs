using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class OpenGiftEvent(IGiftOpeningService gifts) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => gifts.OpenAsync(session, packet.ReadUInt());
}
