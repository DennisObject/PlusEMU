using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Clothing.Parts;

namespace Plus.Communication.Packets.Outgoing.Inventory.AvatarEffects;

public class FigureSetIdsComposer : IServerPacket
{
    private readonly (int PartId, string Part)[] _clothingParts;
    public uint MessageId => ServerPacketHeader.FigureSetIdsComposer;

    public FigureSetIdsComposer(ICollection<ClothingParts> clothingParts)
    {
        _clothingParts = clothingParts.Select(part => (part.PartId, part.Part)).ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_clothingParts.Length);
        foreach (var part in _clothingParts)
            packet.WriteInteger(part.PartId);
        packet.WriteInteger(_clothingParts.Length);
        foreach (var part in _clothingParts)
            packet.WriteString(part.Part);

    }
}
