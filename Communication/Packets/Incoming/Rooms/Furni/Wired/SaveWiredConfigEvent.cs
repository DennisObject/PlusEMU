using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal abstract class SaveWiredConfigEvent(IWiredConfigurationService service) : IPacketEvent
{
    protected abstract WiredBoxCategory Envelope { get; }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        uint itemId;
        try { itemId = packet.ReadUInt(); }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (WiredLegacyProtocol.TryRead(packet, Envelope, out var configuration))
            service.Save(session, new(itemId, Envelope, configuration));
        return Task.CompletedTask;
    }
}
