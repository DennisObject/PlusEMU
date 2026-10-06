namespace Plus.HabboHotel.Talents;

public class TalentTrackLevel
{
    private readonly Dictionary<int, TalentTrackSubLevel> _subLevels;

    public TalentTrackLevel(string type, int level, string dataActions, string dataGifts, IEnumerable<TalentTrackSubLevel> subLevels)
    {
        Type = type;
        Level = level;

        foreach (var str in dataActions.Split('|')) {
            if (Actions == null) {
                Actions = new();
            }

            Actions.Add(str);
        }

        foreach (var str in dataGifts.Split('|')) {
            if (Gifts == null) {
                Gifts = new();
            }

            Gifts.Add(str);
        }

        _subLevels = subLevels.ToDictionary(subLevel => subLevel.Level);
    }

    public string Type { get; set; }
    public int Level { get; set; }

    public List<string> Actions { get; }

    public List<string> Gifts { get; }


    public ICollection<TalentTrackSubLevel> GetSubLevels() => _subLevels.Values;
}
