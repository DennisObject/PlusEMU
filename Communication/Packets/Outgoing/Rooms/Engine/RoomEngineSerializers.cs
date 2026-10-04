using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public static class RoomEngineSerializers
{
    public static void Serialize(this IOutgoingPacket packet, RoomItemSnapshot item)
    {
        packet.WriteUInteger(item.Id);
        packet.WriteInteger(item.SpriteId);
        packet.WriteInteger(item.X);
        packet.WriteInteger(item.Y);
        packet.WriteInteger(item.Rotation);
        packet.WriteString(item.Z);
        packet.WriteString(item.Height);
        packet.WriteInteger(item.FloorExtra);
        FurnitureDataSerializer.Write(packet, item.Data, item.UniqueNumber, item.UniqueSeries);
        packet.WriteInteger(-1);
        packet.WriteInteger(item.UseButton);
        packet.WriteInteger(item.UserId);
        WriteFurnitureMetadata(packet, item.Metadata);
    }

    internal static void WriteWallItem(IOutgoingPacket packet, RoomItemSnapshot item)
    {
        packet.WriteString(item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        packet.WriteInteger(item.SpriteId);
        packet.WriteString(item.WallCoordinates);
        packet.WriteString(item.WallData);
        packet.WriteInteger(-1);
        packet.WriteInteger(item.UseButton);
        packet.WriteInteger(item.UserId);
        WriteFurnitureMetadata(packet, item.Metadata);
    }

    internal static void WriteFurnitureMetadata(IOutgoingPacket packet, FurnitureMetadata metadata)
    {
        packet.WriteInteger(metadata.Stackable ? 1 : 0);
        packet.WriteInteger(metadata.IsSeat ? 1 : 0);
        packet.WriteInteger(metadata.IsBed ? 1 : 0);
        packet.WriteInteger(metadata.Walkable ? 1 : 0);
        packet.WriteInteger(metadata.Width);
        packet.WriteInteger(metadata.Length);
        packet.WriteInteger(0);
    }

    internal static void WriteOwnerMap(IOutgoingPacket packet, RoomFurnitureSnapshot furniture)
    {
        packet.WriteInteger(furniture.Owners.Length);
        foreach (var owner in furniture.Owners) { packet.WriteInteger(owner.Id); packet.WriteString(owner.Name); }
    }
}
