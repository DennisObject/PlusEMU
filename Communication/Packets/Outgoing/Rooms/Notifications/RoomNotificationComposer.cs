using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Notifications;

public class RoomNotificationComposer : IServerPacket
{
    private readonly string _type;
    private readonly ImmutableArray<KeyValuePair<string, string>> _values;

    public uint MessageId => ServerPacketHeader.RoomNotificationComposer;

    public RoomNotificationComposer(string type, string key, string value)
        : this(type, new Dictionary<string, string> { { key, value } })
    {
    }

    public RoomNotificationComposer(string type, Dictionary<string, string> values)
    {
        _type = type;
        _values = values.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToImmutableArray();
    }

    public RoomNotificationComposer(string type) : this(type, new Dictionary<string, string>())
    {
    }

    public RoomNotificationComposer(string title, string message, string type, string hotelName = "", string hotelUrl = "")
        : this(type, new Dictionary<string, string>
        {
            { "title", title },
            { "message", message },
            { "linkUrl", hotelUrl },
            { "linkTitle", hotelName }
        })
    {
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_type);
        packet.WriteInteger(_values.Length);

        foreach (var (key, value) in _values) {
            packet.WriteString(key);
            packet.WriteString(value);
        }
    }
}
