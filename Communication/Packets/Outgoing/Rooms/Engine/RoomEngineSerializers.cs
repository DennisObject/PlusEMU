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
        WriteData(packet, item.Data, item.UniqueNumber, item.UniqueSeries);
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

    private static void WriteData(IOutgoingPacket packet, FurnitureDataSnapshot data, uint uniqueNumber, uint uniqueSeries)
    {
        packet.WriteInt((int)data.Structure | (uniqueSeries > 0 ? 0xFF00 : 0));
        switch (data)
        {
            case FurnitureDataSnapshot.Empty: break;
            case FurnitureDataSnapshot.Legacy legacy: packet.WriteString(legacy.Value); break;
            case FurnitureDataSnapshot.Map map:
                packet.WriteInt(map.Values.Length);
                foreach (var pair in map.Values) { packet.WriteString(pair.Key); packet.WriteString(pair.Value); }
                break;
            case FurnitureDataSnapshot.Strings strings:
                packet.WriteInt(strings.Values.Length);
                foreach (var value in strings.Values) packet.WriteString(value);
                break;
            case FurnitureDataSnapshot.Vote vote: packet.WriteString(vote.State); packet.WriteInt(vote.Result); break;
            case FurnitureDataSnapshot.Integers integers:
                packet.WriteInt(integers.Values.Length);
                foreach (var value in integers.Values) packet.WriteInt(value);
                break;
            case FurnitureDataSnapshot.Highscore score:
                packet.WriteString(score.State); packet.WriteUInt(score.ScoreType); packet.WriteUInt(score.ClearType); packet.WriteUInt(0);
                break;
            case FurnitureDataSnapshot.Crackable crackable:
                packet.WriteString(crackable.State); packet.WriteUInt(crackable.Hits); packet.WriteUInt(crackable.Target);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(data));
        }
        if (uniqueSeries > 0) { packet.WriteUInt(uniqueNumber); packet.WriteUInt(uniqueSeries); }
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
