using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public sealed class HousekeepingUserOverridesComposer : IServerPacket
{
    private readonly int _requestId;
    private readonly int _userId;
    private readonly string _username;
    private readonly (string Key, string Effect, string Reason, DateTimeOffset? ExpiresAt)[] _overrides;

    public HousekeepingUserOverridesComposer(int requestId, AccessOverridePage page)
    {
        _requestId = requestId;
        _userId = page.UserId;
        _username = page.Username;
        _overrides = page.Overrides.Select(row => (row.Key, row.Effect, row.Reason, row.ExpiresAt)).ToArray();
    }

    public uint MessageId => ServerPacketHeader.HousekeepingUserOverridesComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_requestId);
        packet.WriteInteger(_userId);
        packet.WriteString(_username);
        packet.WriteInteger(_overrides.Length);
        foreach (var row in _overrides)
        {
            packet.WriteString(row.Key);
            packet.WriteString(row.Effect);
            packet.WriteString(row.Reason);
            packet.WriteInteger((int)Math.Clamp(row.ExpiresAt?.ToUnixTimeSeconds() ?? 0, 0, int.MaxValue));
        }
    }
}
