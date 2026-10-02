using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Outgoing.Quests;

public readonly record struct RewardTrackView(RewardTrack Track, UserRewardTrackState State);

public sealed class RewardTracksComposer : IServerPacket
{
    private readonly bool _disabled;
    private readonly IReadOnlyList<RewardTrackView> _tracks;
    private readonly bool _reload;
    public uint MessageId => ServerPacketHeader.RewardTracksComposer;

    public RewardTracksComposer(bool disabled, IReadOnlyList<RewardTrackView> tracks, bool reload)
    {
        _disabled = disabled;
        _tracks = tracks;
        _reload = reload;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_disabled);
        packet.WriteInteger(_tracks.Count);
        foreach (var view in _tracks)
            WriteTrack(packet, view.Track, view.State);
        packet.WriteBoolean(_reload);
    }

    public static void WriteTrack(IOutgoingPacket packet, RewardTrack track, UserRewardTrackState state)
    {
        packet.WriteString(track.Id);
        packet.WriteString(track.Theme);
        packet.WriteInteger(state.Points);
        packet.WriteBoolean(track.HasPremium);
        if (track.HasPremium)
        {
            packet.WriteDouble(track.PremiumTaskPointsBoost);
            packet.WriteInteger(track.PremiumInstantPoints);
            packet.WriteInteger(track.PremiumCostDiamonds);
            packet.WriteInteger(track.PremiumCostCredits);
        }
        packet.WriteBoolean(state.Premium);
        var complete = track.Prizes.Where(prize => !prize.Premium).All(prize => state.IsClaimed(prize.Id));
        var premiumComplete = !track.HasPremium || (complete && track.Prizes.Where(prize => prize.Premium).All(prize => state.IsClaimed(prize.Id)));
        packet.WriteBoolean(complete);
        packet.WriteBoolean(premiumComplete);
        packet.WriteInteger(track.Tasks.Count);
        foreach (var task in track.Tasks)
        {
            packet.WriteString(task.Id);
            packet.WriteString(task.ActionType);
            packet.WriteString(task.Parameter);
            packet.WriteInteger(state.ProgressOf(task.Id));
            packet.WriteBoolean(task.Premium);
            packet.WriteInteger(task.Levels.Count);
            foreach (var level in task.Levels)
            {
                packet.WriteInteger(level.RequiredCount);
                packet.WriteInteger(level.PointsReward);
                packet.WriteBoolean(level.Premium);
            }
        }
        packet.WriteInteger(track.Prizes.Count);
        foreach (var prize in track.Prizes)
        {
            var premiumLocked = prize.Premium && !state.Premium;
            packet.WriteString(prize.Id);
            packet.WriteInteger(prize.RequiredPoints);
            packet.WriteShort((short)prize.ProductItemTypeId);
            packet.WriteString(prize.RewardType);
            packet.WriteString(prize.ExtraParams);
            packet.WriteInteger(prize.RewardAmount);
            packet.WriteBoolean(prize.Premium);
            packet.WriteBoolean(!premiumLocked && state.Points >= prize.RequiredPoints);
            packet.WriteBoolean(state.IsClaimed(prize.Id));
        }
    }
}
