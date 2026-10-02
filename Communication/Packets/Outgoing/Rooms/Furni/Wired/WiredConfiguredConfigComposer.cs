using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredConfiguredConfigComposer : IServerPacket
{
    private readonly uint _itemId;
    private readonly int _spriteId;
    private readonly WiredBoxDescriptor _descriptor;
    private readonly WiredConfiguration _configuration;

    public WiredConfiguredConfigComposer(IWiredConfiguredItem box)
    {
        _itemId = box.Item.Id;
        _spriteId = box.Item.Definition.SpriteId;
        _descriptor = box.Descriptor;
        _configuration = box.Configuration;
    }

    // Use the existing revision translations for the three envelopes.
    public uint MessageId => _descriptor.Envelope switch
    {
        WiredBoxCategory.Trigger => ServerPacketHeader.WiredTriggeRconfigComposer,
        WiredBoxCategory.Condition => ServerPacketHeader.WiredConditionConfigComposer,
        _ => ServerPacketHeader.WiredEffectConfigComposer
    };

    public void Compose(IOutgoingPacket packet) =>
        WiredLegacyProtocol.Write(packet, _itemId, _spriteId, _descriptor, _configuration,
            WiredConfigurationLimits.SelectedItems, []);
}
