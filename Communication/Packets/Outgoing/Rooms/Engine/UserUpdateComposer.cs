using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public sealed class UserUpdateComposer(ImmutableArray<RoomUserStatusSnapshot> users) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.UserUpdateComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(users.Length);

        foreach (var user in users)
        {
            packet.WriteInteger(user.VirtualId);
            packet.WriteInteger(user.X);
            packet.WriteInteger(user.Y);
            packet.WriteString(user.Z);
            packet.WriteInteger(user.HeadRotation);
            packet.WriteInteger(user.BodyRotation);
            packet.WriteString(user.Status);
        }
    }
}
