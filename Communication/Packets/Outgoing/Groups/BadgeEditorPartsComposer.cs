using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class BadgeEditorPartsComposer(BadgeEditorPresentation presentation) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.BadgeEditorPartsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(presentation.Bases.Length);

        foreach (var part in presentation.Bases) {
            packet.WriteInteger(part.Id);
            packet.WriteString(part.AssetOne);
            packet.WriteString(part.AssetTwo);
        }

        packet.WriteInteger(presentation.Symbols.Length);

        foreach (var part in presentation.Symbols) {
            packet.WriteInteger(part.Id);
            packet.WriteString(part.AssetOne);
            packet.WriteString(part.AssetTwo);
        }

        packet.WriteInteger(presentation.BaseColours.Length);

        foreach (var color in presentation.BaseColours) {
            packet.WriteInteger(color.Id);
            packet.WriteString(color.Colour);
        }

        packet.WriteInteger(presentation.SymbolColours.Length);

        foreach (var color in presentation.SymbolColours) {
            packet.WriteInteger(color.Id);
            packet.WriteString(color.Colour);
        }

        packet.WriteInteger(presentation.BackgroundColours.Length);

        foreach (var color in presentation.BackgroundColours) {
            packet.WriteInteger(color.Id);
            packet.WriteString(color.Colour);
        }
    }
}
