namespace Plus.HabboHotel.Talents;

public class TalentTrackSubLevel
{
    public TalentTrackSubLevel(int level, string badge, int requiredProgress)
    {
        Level = level;
        Badge = badge;
        RequiredProgress = requiredProgress;
    }

    public int Level
    {
        get; set;
    }
    public string Badge
    {
        get; set;
    }
    public int RequiredProgress
    {
        get; set;
    }
}
