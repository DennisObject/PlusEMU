using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredConfiguredConfigComposer : IServerPacket
{
    private readonly uint _itemId;
    private readonly int _spriteId;
    private readonly WiredBoxDescriptor _descriptor;
    private readonly WiredConfiguration _configuration;
    private readonly int _furniLimit;

    public WiredConfiguredConfigComposer(IWiredConfiguredItem box)
        : this(box.Item, box.Descriptor,
            box is IWiredEditorConfigurationProvider editor ? editor.GetEditorConfiguration() : box.Configuration)
    {
    }

    public WiredConfiguredConfigComposer(Item item, WiredBoxDescriptor descriptor, WiredConfiguration configuration,
        int furniLimit = WiredConfigurationLimits.SelectedItems)
    {
        _itemId = item.Id;
        _spriteId = item.Definition.SpriteId;
        _descriptor = descriptor;
        _configuration = configuration;
        _furniLimit = furniLimit;
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
            _furniLimit, []);
}
