using Plus.HabboHotel.GameClients;
namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public class RoomRightsListComposer : IServerPacket
{
    private readonly uint _roomId;
    private readonly IReadOnlyCollection<RoomRightHolder> _users;
    public uint MessageId => ServerPacketHeader.RoomRightsListComposer;

    public RoomRightsListComposer(uint roomId, IReadOnlyCollection<RoomRightHolder> users)
    {
        _roomId = roomId;
        _users = users.ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
        packet.WriteInteger(_users.Count);

        foreach (var user in _users) {
            packet.WriteInteger(user.Id);
            packet.WriteString(user.Username);
        }
    }
}

public sealed record RoomRightHolder(int Id, string Username);
