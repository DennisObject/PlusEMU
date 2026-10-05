using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredConfiguredConfigComposer(WiredEditorSnapshot snapshot) : IServerPacket
{
    public uint MessageId => snapshot.Descriptor.Envelope switch
    {
        WiredBoxCategory.Trigger => ServerPacketHeader.WiredTriggeRconfigComposer,
        WiredBoxCategory.Condition => ServerPacketHeader.WiredConditionConfigComposer,
        _ => ServerPacketHeader.WiredEffectConfigComposer
    };

    public void Compose(IOutgoingPacket packet) =>
        WiredLegacyProtocol.Write(packet, snapshot.ItemId, snapshot.SpriteId, snapshot.Descriptor,
            snapshot.Configuration, snapshot.FurniLimit, snapshot.BlockedItems);
}
