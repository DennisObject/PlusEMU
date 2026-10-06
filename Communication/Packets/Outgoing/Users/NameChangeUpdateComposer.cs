using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Users;

public class NameChangeUpdateComposer : IServerPacket
{
    private readonly string _name;
    private readonly NameChangeError _error;
    private readonly ICollection<string> _tags;

    public uint MessageId => ServerPacketHeader.NameChangeUpdateComposer;

    public NameChangeUpdateComposer(string name, NameChangeError error, ICollection<string> tags)
    {
        _name = name;
        _error = error;
        _tags = tags.ToArray();
    }

    public NameChangeUpdateComposer(string name, NameChangeError error)
    {
        _name = name;
        _error = error;
        _tags = Array.Empty<string>();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger((int)_error);
        packet.WriteString(_name);
        packet.WriteInteger(_tags.Count);

        foreach (var tag in _tags)
        {
            packet.WriteString(_name + tag);
        }
    }
}
