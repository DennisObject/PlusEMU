namespace Plus.Core.FigureData.Types;

internal class Set
{
    public Set(int id, string gender, int clubLevel, bool colorable, bool selectable, bool preselectable, bool sellable = false)
    {
        Id = id;
        Gender = gender;
        ClubLevel = clubLevel;
        Colorable = colorable;
        Selectable = selectable;
        Preselectable = preselectable;
        Sellable = sellable;
        Parts = new();
    }

    public int Id { get; set; }
    public string Gender { get; set; }
    public int ClubLevel { get; set; }
    public bool Colorable { get; set; }
    public bool Selectable { get; set; }
    public bool Preselectable { get; set; }
    public bool Sellable { get; set; }
    public Dictionary<string, Part> Parts { get; set; }
}
