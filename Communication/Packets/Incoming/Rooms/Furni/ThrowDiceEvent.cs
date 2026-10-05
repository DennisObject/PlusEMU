using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class ThrowDiceEvent(IFurnitureUseService furniture) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        furniture.RollDice(room, session, new(packet.ReadUInt(), packet.ReadInt()));
        return Task.CompletedTask;
    }
}