using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public sealed class UserChangeComposer(AvatarChangeSnapshot avatar) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.UserChangeComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(avatar.VirtualId);
        packet.WriteString(avatar.Look);
        packet.WriteString(avatar.Gender);
        packet.WriteString(avatar.Motto);
        packet.WriteInteger(avatar.AchievementPoints);
    }
}
