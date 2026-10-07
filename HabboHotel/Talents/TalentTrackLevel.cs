namespace Plus.HabboHotel.Talents;

public class TalentTrackLevel
{
    private readonly Dictionary<int, TalentTrackSubLevel> _subLevels;

    public TalentTrackLevel(string type, int level, string dataActions, string dataGifts, IEnumerable<TalentTrackSubLevel> subLevels)
    {
        Type = type;
        Level = level;

        Actions = dataActions.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        Gifts = dataGifts.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

        _subLevels = subLevels.ToDictionary(subLevel => subLevel.Level);
    }

    public string Type { get; set; }
    public int Level { get; set; }

    public List<string> Actions { get; }

    public List<string> Gifts { get; }


    public ICollection<TalentTrackSubLevel> GetSubLevels() => _subLevels.Values;
}
