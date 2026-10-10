using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public class WiredConditionConfigComposer(WiredEditorSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredConditionConfigComposer;

    public void Compose(IOutgoingPacket packet) =>
        WiredLegacyProtocol.Write(packet, snapshot.ItemId, snapshot.SpriteId, snapshot.Descriptor,
            snapshot.Configuration, snapshot.FurniLimit, snapshot.BlockedItems, snapshot.Native, snapshot.CatalogHash, snapshot.SharedVariables);
}
