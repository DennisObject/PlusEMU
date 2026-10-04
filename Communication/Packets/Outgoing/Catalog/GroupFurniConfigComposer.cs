using Plus.HabboHotel.GameClients;
namespace Plus.Communication.Packets.Outgoing.Catalog;

public class GroupFurniConfigComposer : IServerPacket
{
    private readonly IReadOnlyCollection<GroupFurniConfig> _groups;
    public uint MessageId => ServerPacketHeader.GroupFurniConfigComposer;

    public GroupFurniConfigComposer(IReadOnlyCollection<GroupFurniConfig> groups)
    {
        _groups = groups;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_groups.Count);
        foreach (var group in _groups)
        {
            packet.WriteInteger(group.Id);
            packet.WriteString(group.Name);
            packet.WriteString(group.Badge);
            packet.WriteString(group.PrimaryColour);
            packet.WriteString(group.SecondaryColour);
            packet.WriteBoolean(false);
            packet.WriteInteger(group.CreatorId);
            packet.WriteBoolean(group.ForumEnabled);
        }
    }
}

public sealed record GroupFurniConfig(
    int Id,
    string Name,
    string Badge,
    string PrimaryColour,
    string SecondaryColour,
    int CreatorId,
    bool ForumEnabled);
