using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing;

internal static class FurnitureDataSerializer
{
    public static void Write(IOutgoingPacket packet, FurnitureDataSnapshot data, uint uniqueNumber, uint uniqueSeries)
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

}
