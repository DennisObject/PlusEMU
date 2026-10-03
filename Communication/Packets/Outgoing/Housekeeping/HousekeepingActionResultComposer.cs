using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public class HousekeepingActionResultComposer : IServerPacket
{
    private readonly string _actionKey;
    private readonly bool _ok;
    private readonly int _actionId;
    private readonly string _message;
    public uint MessageId => ServerPacketHeader.HousekeepingActionResultComposer;

    public HousekeepingActionResultComposer(string actionKey, bool ok, int actionId, string message)
    {
        _actionKey = actionKey;
        _ok = ok;
        _actionId = actionId;
        _message = message;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_actionKey);
        packet.WriteBoolean(_ok);
        packet.WriteInteger(_actionId);
        packet.WriteString(_message);
    }
}
