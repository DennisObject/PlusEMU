using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Outgoing.FurniEditor;

// Octane-Renderer's FurnitureDataReloadParser. Delta patches names and descriptions in place; the reload hint makes
// clients fetch the furnidata again, needed when other fields of an entry changed.
public sealed class FurnitureDataReloadComposer : IServerPacket
{
    public const int Delta = 0;
    public const int ReloadHint = 1;

    private readonly int _mode;
    private readonly IReadOnlyList<FurnidataEdit> _entries;

    public uint MessageId => ServerPacketHeader.FurnitureDataReloadComposer;

    public FurnitureDataReloadComposer(int mode, IReadOnlyList<FurnidataEdit> entries)
    {
        _mode = mode;
        _entries = entries.ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_mode);

        if (_mode != Delta)
        {
            return;
        }

        packet.WriteInteger(_entries.Count);

        foreach (var entry in _entries)
        {
            packet.WriteString(entry.IsWallItem ? "I" : "S");
            packet.WriteInteger(entry.Id);
            packet.WriteString(entry.Classname);
            packet.WriteString(entry.Name);
            packet.WriteString(entry.Description);
        }
    }
}
