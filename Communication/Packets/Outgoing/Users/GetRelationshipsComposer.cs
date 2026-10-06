using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Outgoing.Users;

public sealed class GetRelationshipsComposer(RelationshipSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GetRelationshipsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(snapshot.UserId);
        packet.WriteInteger(snapshot.Entries.Length);

        foreach (var relationship in snapshot.Entries)
        {
            packet.WriteInteger(relationship.Type);
            packet.WriteInteger(relationship.Count);
            packet.WriteInteger(relationship.UserId);
            packet.WriteString(relationship.Username);
            packet.WriteString(relationship.Look);
        }
    }
}
