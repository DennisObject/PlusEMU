using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Outgoing.Rooms.AI.Bots;

public class OpenBotActionComposer : IServerPacket
{
    private readonly BotActionViewSnapshot _snapshot;
    public uint MessageId => ServerPacketHeader.OpenBotActionComposer;

    public OpenBotActionComposer(BotActionViewSnapshot snapshot) => _snapshot = snapshot;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_snapshot.BotId);
        packet.WriteInteger(_snapshot.ActionId);
        if (_snapshot.ActionId == 2)
            packet.WriteString(_snapshot.Data);
        else if (_snapshot.ActionId == 5)
            packet.WriteString(_snapshot.Data);

    }
}