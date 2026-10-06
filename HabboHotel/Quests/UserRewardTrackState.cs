namespace Plus.HabboHotel.Quests;

public sealed class UserRewardTrackState
{
    private readonly Dictionary<string, int> _progress = new();
    private readonly Dictionary<string, int> _peaks = new();
    private readonly HashSet<string> _claimed = new();

    public UserRewardTrackState(string trackId, int points, bool premium)
    {
        TrackId = trackId;
        Points = Math.Max(0, points);
        Premium = premium;
    }

    public string TrackId
    {
        get;
    }
    public int Points
    {
        get; private set;
    }
    public bool Premium
    {
        get; private set;
    }

    public int ProgressOf(string taskId) => _progress.GetValueOrDefault(taskId);

    public int PeakOf(string taskId) => Math.Max(_peaks.GetValueOrDefault(taskId), ProgressOf(taskId));

    public void SetPeak(string taskId, int peak)
    {
        _peaks[taskId] = Math.Max(PeakOf(taskId), Math.Max(0, peak));
    }

    /// <summary>Lowers the stored count and leaves the peak, so climbing the same levels again pays nothing.</summary>
    public void SetStoredCount(string taskId, int count) => _progress[taskId] = Math.Max(0, count);

    public void Apply(RewardTrackStep step)
    {
        _progress[step.TaskId] = step.Count;
        _peaks[step.TaskId] = Math.Max(PeakOf(step.TaskId), step.Peak);

        if (step.PointsGranted > 0)
        {
            Points += step.PointsGranted;
        }
    }

    public bool IsClaimed(string prizeId) => _claimed.Contains(prizeId);

    public void MarkClaimed(string prizeId) => _claimed.Add(prizeId);

    public void ApplyPremium(int points)
    {
        Premium = true;
        Points = Math.Max(0, points);
    }
}

public readonly record struct RewardTrackStep(bool Changed, string TaskId, int Count, int Peak, int PointsGranted, int TotalPoints);

public readonly record struct PremiumQuote(RewardTrackResults Result, int Credits, int Diamonds, int Points);
