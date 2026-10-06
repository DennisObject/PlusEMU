using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Users;

public class UserTagsComposer : IServerPacket
{
    private readonly int _userId;
    public uint MessageId => ServerPacketHeader.UserTagsComposer;

    public UserTagsComposer(int userId)
    {
        _userId = userId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_userId);
        packet.WriteInteger(0); //Count of the tags.
        {
            //Append a string.
        }
    }
}
