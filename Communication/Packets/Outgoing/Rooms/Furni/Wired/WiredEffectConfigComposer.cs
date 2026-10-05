using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public class WiredEffectConfigComposer(WiredEditorSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredEffectConfigComposer;

    public void Compose(IOutgoingPacket packet) =>
        WiredLegacyProtocol.Write(packet, snapshot.ItemId, snapshot.SpriteId, snapshot.Descriptor,
            snapshot.Configuration, snapshot.FurniLimit, snapshot.BlockedItems);
}
