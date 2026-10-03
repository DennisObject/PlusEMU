using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Outgoing.FurniEditor;

public sealed class FurniEditorResultComposer : IServerPacket
{
    private readonly FurniEditorResult _result;

    public uint MessageId => ServerPacketHeader.FurniEditorResultComposer;

    public FurniEditorResultComposer(FurniEditorResult result) => _result = result;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_result.Success);
        packet.WriteString(_result.Message);
        packet.WriteInteger((int)_result.ItemId);
    }
}
