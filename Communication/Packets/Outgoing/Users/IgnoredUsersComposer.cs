using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Users;

public class IgnoredUsersComposer : IServerPacket
{
    private readonly IReadOnlyCollection<string> _ignoredUsers;

    public uint MessageId => ServerPacketHeader.IgnoredUsersComposer;

    public IgnoredUsersComposer(IReadOnlyCollection<string> ignoredUsers)
    {
        _ignoredUsers = ignoredUsers.ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_ignoredUsers.Count);

        foreach (var username in _ignoredUsers) {
            packet.WriteString(username);
        }
    }
}
