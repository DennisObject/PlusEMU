using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Outgoing.FurniEditor;

public sealed class FurniEditorImportTextResultComposer : IServerPacket
{
    private readonly FurniEditorImportResult _result;

    public uint MessageId => ServerPacketHeader.FurniEditorImportTextResultComposer;

    public FurniEditorImportTextResultComposer(FurniEditorImportResult result) => _result = result;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_result.Found);
        packet.WriteString(_result.Name);
        packet.WriteString(_result.Description);
        packet.WriteString(_result.Classname);
    }
}
