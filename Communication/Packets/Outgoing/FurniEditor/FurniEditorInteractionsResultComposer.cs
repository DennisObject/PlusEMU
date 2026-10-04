using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FurniEditor;

public sealed class FurniEditorInteractionsResultComposer : IServerPacket
{
    private readonly IReadOnlyList<string> _interactions;

    public uint MessageId => ServerPacketHeader.FurniEditorInteractionsResultComposer;

    public FurniEditorInteractionsResultComposer(IReadOnlyList<string> interactions) => _interactions = interactions;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_interactions.Count);
        foreach (var interaction in _interactions)
            packet.WriteString(interaction);
    }
}
