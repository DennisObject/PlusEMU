using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class UserRightsComposer : IServerPacket
{
    private readonly int _rank;
    private readonly bool _isAmbassador;
    private readonly string _rankName;
    private readonly string _rankBadge;
    private readonly IReadOnlyList<string> _permissions;
    public uint MessageId => ServerPacketHeader.UserRightsComposer;

    public UserRightsComposer(int rank, bool isAmbassador, string rankName, string rankBadge, IReadOnlyList<string> permissions)
    {
        _rank = rank;
        _isAmbassador = isAmbassador;
        _rankName = rankName;
        _rankBadge = rankBadge;
        _permissions = permissions;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(2); //Club level
        packet.WriteInteger(_rank);
        packet.WriteBoolean(_isAmbassador); //Is an ambassador
        // Octane's optional rank metadata and resolved permission block (1 = allowed).
        packet.WriteInteger(_rank);
        packet.WriteString(_rankName);
        packet.WriteString(_rankBadge);
        packet.WriteInteger(_permissions.Count);
        foreach (var permission in _permissions)
        {
            packet.WriteString(permission);
            packet.WriteInteger(1);
        }
    }
}