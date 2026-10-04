using Plus.Communication.Packets;
using System.Collections.Immutable;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public sealed class UsersComposer : IServerPacket
{
    private readonly ImmutableArray<RoomUserSnapshot> _users;

    public UsersComposer(RoomUserSnapshot user) : this([user]) { }
    public UsersComposer(IReadOnlyList<RoomUserSnapshot> users) => _users = users.ToImmutableArray();
    public uint MessageId => ServerPacketHeader.UsersComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_users.Length);
        foreach (var user in _users)
        {
            packet.WriteInteger(user.Id); packet.WriteString(user.Name); packet.WriteString(user.Motto);
            packet.WriteString(user.Look); packet.WriteInteger(user.VirtualId); packet.WriteInteger(user.X);
            packet.WriteInteger(user.Y); packet.WriteString(user.Z); packet.WriteInteger(user.Rotation);
            packet.WriteInteger(user.Kind);
            if (user.Kind == 1)
            {
                packet.WriteString(user.Gender); packet.WriteInteger(user.GroupId); packet.WriteInteger(0);
                packet.WriteString(user.GroupName); packet.WriteString(""); packet.WriteInteger(user.AchievementPoints);
                packet.WriteBoolean(false);
            }
            else if (user.IsPet)
            {
                packet.WriteInteger(user.PetType); packet.WriteInteger(user.OwnerId); packet.WriteString(user.OwnerName);
                packet.WriteInteger(1); packet.WriteBoolean(user.HasSaddle); packet.WriteBoolean(user.IsRidden);
                packet.WriteInteger(0); packet.WriteInteger(0); packet.WriteString("");
            }
            else
            {
                packet.WriteString(user.Gender); packet.WriteInteger(user.OwnerId); packet.WriteString(user.OwnerName);
                packet.WriteInteger(5); packet.WriteShort(1); packet.WriteShort(2); packet.WriteShort(3);
                packet.WriteShort(4); packet.WriteShort(5);
            }
            packet.WriteString(""); packet.WriteInteger(0);
        }
    }
}
