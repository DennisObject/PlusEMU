using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Outgoing.Quests;

public sealed class RewardTracksComposer : IServerPacket
{
    private readonly bool _disabled;
    private readonly ImmutableArray<RewardTrackWireTrack> _tracks;
    private readonly bool _reload;
    public uint MessageId => ServerPacketHeader.RewardTracksComposer;

    public RewardTracksComposer(bool disabled, ImmutableArray<RewardTrackWireTrack> tracks, bool reload)
    {
        _disabled = disabled;
        _tracks = tracks;
        _reload = reload;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_disabled);
        packet.WriteInteger(_tracks.Length);
        foreach (var track in _tracks)
            WriteTrack(packet, track);
        packet.WriteBoolean(_reload);
    }

    private static void WriteTrack(IOutgoingPacket packet, RewardTrackWireTrack track)
    {
        packet.WriteString(track.Id);
        packet.WriteString(track.Theme);
        packet.WriteInteger(track.Points);
        packet.WriteBoolean(track.HasPremium);
        if (track.HasPremium)
        {
            packet.WriteDouble(track.PremiumTaskPointsBoost);
            packet.WriteInteger(track.PremiumInstantPoints);
            packet.WriteInteger(track.PremiumCostDiamonds);
            packet.WriteInteger(track.PremiumCostCredits);
        }
        packet.WriteBoolean(track.Premium);
        packet.WriteBoolean(track.Complete);
        packet.WriteBoolean(track.PremiumComplete);
        packet.WriteInteger(track.Tasks.Length);
        foreach (var task in track.Tasks)
        {
            packet.WriteString(task.Id);
            packet.WriteString(task.ActionType);
            packet.WriteString(task.Parameter);
            packet.WriteInteger(task.Progress);
            packet.WriteBoolean(task.Premium);
            packet.WriteInteger(task.Levels.Length);
            foreach (var level in task.Levels)
            {
                packet.WriteInteger(level.RequiredCount);
                packet.WriteInteger(level.PointsReward);
                packet.WriteBoolean(level.Premium);
            }
        }
        packet.WriteInteger(track.Prizes.Length);
        foreach (var prize in track.Prizes)
        {
            packet.WriteString(prize.Id);
            packet.WriteInteger(prize.RequiredPoints);
            packet.WriteShort((short)prize.ProductItemTypeId);
            packet.WriteString(prize.RewardType);
            packet.WriteString(prize.ExtraParams);
            packet.WriteInteger(prize.RewardAmount);
            packet.WriteBoolean(prize.Premium);
            packet.WriteBoolean(prize.Reachable);
            packet.WriteBoolean(prize.Claimed);
        }
    }
}
