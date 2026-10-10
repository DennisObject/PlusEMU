using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal abstract class SaveWiredConfigEvent(IWiredConfigurationService service) : IPacketEvent
{
    protected abstract WiredBoxCategory Envelope { get; }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        uint itemId;

        try {
            itemId = packet.ReadUInt();
        }
        catch (ArgumentException) {
            return Task.CompletedTask;
        }

        if (itemId is > 0 and <= int.MaxValue && WiredLegacyProtocol.TryReadNative(packet, Envelope, out var native)) {
            service.Save(session, new(itemId, Envelope, WiredLegacyProtocol.WireDraft(native), native));
        }

        return Task.CompletedTask;
    }
}

internal sealed class SaveWiredSelectorConfigEvent(IWiredConfigurationService service) : SaveWiredConfigEvent(service)
{
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Selector;
}

internal sealed class SaveWiredAddonConfigEvent(IWiredConfigurationService service) : SaveWiredConfigEvent(service)
{
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Addon;
}

internal sealed class SaveWiredVariableConfigEvent(IWiredConfigurationService service) : SaveWiredConfigEvent(service)
{
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Variable;
}
