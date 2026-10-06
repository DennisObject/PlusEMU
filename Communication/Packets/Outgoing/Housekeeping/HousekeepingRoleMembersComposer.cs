using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public sealed class HousekeepingRoleMembersComposer : IServerPacket
{
    private readonly int _requestId;
    private readonly int _roleId;
    private readonly int _offset;
    private readonly int _total;
    private readonly (int Id, string Username, DateTimeOffset? ExpiresAt)[] _members;

    public HousekeepingRoleMembersComposer(int requestId, AccessMemberPage page)
    {
        _requestId = requestId;
        _roleId = page.RoleId;
        _offset = page.Offset;
        _total = page.Total;
        _members = page.Members.Select(member => (member.Id, member.Username, member.ExpiresAt)).ToArray();
    }

    public uint MessageId => ServerPacketHeader.HousekeepingRoleMembersComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_requestId);
        packet.WriteInteger(_roleId);
        packet.WriteInteger(_offset);
        packet.WriteInteger(_total);
        packet.WriteInteger(_members.Length);

        foreach (var member in _members)
        {
            packet.WriteInteger(member.Id);
            packet.WriteString(member.Username);
            packet.WriteInteger((int)Math.Clamp(member.ExpiresAt?.ToUnixTimeSeconds() ?? 0, 0, int.MaxValue));
        }
    }
}
