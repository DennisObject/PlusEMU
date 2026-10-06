using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class GroupCreationWindowComposer : IServerPacket
{
    private readonly GroupCreationPresentation _presentation;
    public uint MessageId => ServerPacketHeader.GroupCreationWindowComposer;

    public GroupCreationWindowComposer(GroupCreationPresentation presentation)
    {
        _presentation = presentation;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_presentation.Price);
        packet.WriteInteger(_presentation.Rooms.Length); //Room count that the user has.
        foreach (var room in _presentation.Rooms)
        {
            packet.WriteUInteger(room.Id); //Room Id
            packet.WriteString(room.Name); //Room Name
            packet.WriteBoolean(false); //What?
        }
        packet.WriteInteger(5);
        packet.WriteInteger(5);
        packet.WriteInteger(11);
        packet.WriteInteger(4);
        packet.WriteInteger(6);
        packet.WriteInteger(11);
        packet.WriteInteger(4);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
    }
}
