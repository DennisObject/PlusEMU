using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Outgoing.FurniEditor;

public sealed class FurniEditorSearchResultComposer : IServerPacket
{
    private readonly FurniEditorSearchResult _result;

    public uint MessageId => ServerPacketHeader.FurniEditorSearchResultComposer;

    public FurniEditorSearchResultComposer(FurniEditorSearchResult result) => _result = result;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_result.Items.Count);

        foreach (var item in _result.Items) {
            FurniEditorItemWriter.Write(packet, item);
        }

        packet.WriteInteger(_result.Total);
        packet.WriteInteger(_result.Page);
    }
}
